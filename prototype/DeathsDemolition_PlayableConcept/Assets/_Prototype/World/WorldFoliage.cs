using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// Everything that grows: the sakura garden, the bamboo, the Great Pine, the hedges,
    /// the rocks, the ferns and the stone lanterns.
    ///
    /// WHERE A PLANT IS ALLOWED TO STAND
    /// ---------------------------------
    /// Three tests, in this order, and all three are cheap:
    ///
    ///   1. <see cref="WorldLayout.Classify"/> must return Moss at the trunk AND at four
    ///      probes 1.4 m out. Moss is by definition "not a route", so this single test
    ///      keeps every trunk off every road, out of the courtyard and out of the pond,
    ///      and it automatically respects the varying road widths instead of duplicating
    ///      them.
    ///   2. <see cref="Physics.CheckSphere"/> against the structures that were built a
    ///      moment ago. This is deliberately a query against the REAL colliders rather
    ///      than a second hand-maintained table of building footprints: a table drifts the
    ///      first time somebody moves the pagoda, a collider query cannot.
    ///   3. A keep-out radius around the spawn points, the Special Weapon points and the
    ///      player start, so a tree never grows on top of a gameplay position.
    ///
    /// WHY CANOPIES ARE CLUSTERED ON A 15 m GRID
    /// -----------------------------------------
    /// Canopies are the most common thing that gets between a 15 m high camera and the
    /// player, so they must be able to fade. Fading is per GameObject, so one object for
    /// the whole grove would blink the entire east of the map at once, and one object per
    /// tree would be 200 renderers. A 15 m bucket is the compromise: the piece that fades
    /// is about the size of the screen area it was blocking.
    /// </summary>
    public static class WorldFoliage
    {
        /// <summary>PROTOTYPE-ONLY, invented: side of a canopy fade bucket, in metres.</summary>
        private const float ClusterSize = 15f;

        /// <summary>PROTOTYPE-ONLY, invented: clearance a trunk needs from any route.</summary>
        private const float RouteClearance = 1.4f;

        /// <summary>PROTOTYPE-ONLY, invented: clearance from spawn and pickup positions.</summary>
        public const float GameplayClearance = 2.8f;

        private static int s_blockMask;
        private static List<Vector2> s_keepOut;

        public static void Build(Transform root, WorldPalette palette, List<Vector2> keepOut)
        {
            s_keepOut = keepOut ?? new List<Vector2>();

            int env = PrototypeLayers.Environment;
            int occ = PrototypeLayers.CameraOccluder;
            s_blockMask = (env >= 0 ? 1 << env : 0) | (occ >= 0 ? 1 << occ : 0);

            // The structures were created this frame. Without this the queries below see
            // an empty physics scene and trees grow through the palace.
            Physics.SyncTransforms();

            BuildSakura(WorldPiece.Group(root, "Sakura Garden (east)"), palette);
            BuildBamboo(WorldPiece.Group(root, "Bamboo Garden (west)"), palette);
            BuildGreatPine(WorldPiece.Group(root, "Great Pine (centre landmark)"), palette);
            BuildHedgesAndRocks(WorldPiece.Group(root, "Rocks and Hedges"), palette);
            BuildGroundCover(WorldPiece.Group(root, "Ground Cover"), palette);
            BuildLanterns(WorldPiece.Group(root, "Stone Lanterns"), palette);
        }

        // ------------------------------------------------------------------ tests

        private static bool IsPlantable(float x, float z, float radius)
        {
            if (WorldLayout.Classify(x, z) != GroundSurface.Moss)
            {
                return false;
            }

            if (WorldLayout.Classify(x + RouteClearance, z) != GroundSurface.Moss ||
                WorldLayout.Classify(x - RouteClearance, z) != GroundSurface.Moss ||
                WorldLayout.Classify(x, z + RouteClearance) != GroundSurface.Moss ||
                WorldLayout.Classify(x, z - RouteClearance) != GroundSurface.Moss)
            {
                return false;
            }

            for (int i = 0; i < s_keepOut.Count; i++)
            {
                Vector2 k = s_keepOut[i];
                float dx = k.x - x;
                float dz = k.y - z;
                if (dx * dx + dz * dz < GameplayClearance * GameplayClearance)
                {
                    return false;
                }
            }

            if (s_blockMask != 0 &&
                Physics.CheckSphere(new Vector3(x, 1.2f, z), radius, s_blockMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return true;
        }

        private static int ClusterIndex(float x, float z)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((x + WorldLayout.HalfMap) / ClusterSize), 0, 5);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z + WorldLayout.HalfMap) / ClusterSize), 0, 5);
            return cz * 6 + cx;
        }

        private static MeshBuilder Bucket(MeshBuilder[] buckets, int index)
        {
            if (buckets[index] == null)
            {
                buckets[index] = new MeshBuilder(1024);
            }

            return buckets[index];
        }

        private static void FlushClusters(Transform group, MeshBuilder[] buckets, string prefix,
            Material material, WorldPiece.Solidity solidity, SurfaceKind surface)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] == null || buckets[i].IsEmpty)
                {
                    continue;
                }

                WorldPiece.Create(group, prefix + "_" + i, buckets[i], material, solidity, surface);
            }
        }

        // ------------------------------------------------------------------ sakura

        private static void BuildSakura(Transform group, WorldPalette palette)
        {
            var trunks = new MeshBuilder(4096);
            var canopyPink = new MeshBuilder[36];
            var canopyPurple = new MeshBuilder[36];

            // The Great Sakura first: the east landmark, planted by hand at the point
            // where the ring forks around it, and roughly twice everything else.
            AddSakura(trunks, canopyPink, canopyPurple, 34.5f, 0.5f, 1.9f, true);

            for (float x = 14f; x < 44f; x += 3.0f)
            {
                for (float z = -30f; z < 30f; z += 3.0f)
                {
                    float n = WorldLayout.Noise(x, z, 0.09f, 4211);
                    if (n < 0.42f)
                    {
                        continue;
                    }

                    float px = x + (WorldLayout.Hash(Mathf.RoundToInt(x), Mathf.RoundToInt(z), 13) - 0.5f) * 2.4f;
                    float pz = z + (WorldLayout.Hash(Mathf.RoundToInt(x), Mathf.RoundToInt(z), 29) - 0.5f) * 2.4f;

                    if (!IsPlantable(px, pz, 1.1f))
                    {
                        continue;
                    }

                    float scale = 0.75f + n * 0.6f;
                    AddSakura(trunks, canopyPink, canopyPurple, px, pz, scale, false);
                }
            }

            WorldPiece.Create(group, "SakuraTrunks", trunks, palette.Wood,
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
            FlushClusters(group, canopyPink, "SakuraCanopy", palette.Sakura,
                WorldPiece.Solidity.Occluder, SurfaceKind.Foliage);
            FlushClusters(group, canopyPurple, "SakuraCanopyDeep", palette.SakuraDeep,
                WorldPiece.Solidity.Occluder, SurfaceKind.Foliage);
        }

        private static void AddSakura(MeshBuilder trunks, MeshBuilder[] pink, MeshBuilder[] purple,
            float x, float z, float scale, bool great)
        {
            int seed = Mathf.RoundToInt(x * 7f + z * 13f);
            float lean = (WorldLayout.Hash(seed, 1, 77) - 0.5f) * 12f;
            float trunkH = (great ? 4.6f : 2.6f) * scale;
            float trunkR = (great ? 0.62f : 0.24f) * scale;

            Quaternion tilt = Quaternion.Euler(lean, WorldLayout.Hash(seed, 2, 91) * 360f, lean * 0.4f);
            var baseP = new Vector3(x, WorldLayout.GroundY - 0.15f, z);

            trunks.AddCylinder(baseP, trunkR, trunkR * 0.72f, trunkH, great ? 10 : 7, true, tilt);

            int branches = great ? 5 : 3;
            for (int b = 0; b < branches; b++)
            {
                float a = WorldLayout.Hash(seed, b + 3, 133) * Mathf.PI * 2f;
                float len = (great ? 2.6f : 1.4f) * scale;
                var from = new Vector3(x, WorldLayout.GroundY + trunkH * 0.65f, z);
                Quaternion dir = Quaternion.Euler(52f, a * Mathf.Rad2Deg, 0f);
                trunks.AddCylinder(from, trunkR * 0.5f, trunkR * 0.28f, len, 5, true, dir);
            }

            // Canopy: three overlapping ellipsoids, flattened, so the silhouette is a
            // cloud rather than a ball. Pink is the body, purple is a minority accent -
            // the request asks for both and pink alone reads as candy.
            int cluster = ClusterIndex(x, z);
            float canopyY = WorldLayout.GroundY + trunkH + (great ? 1.9f : 0.95f) * scale;
            float rx = (great ? 5.4f : 2.35f) * scale;
            float ry = (great ? 2.1f : 1.05f) * scale;

            for (int i = 0; i < 3; i++)
            {
                float ox = (WorldLayout.Hash(seed, i + 20, 17) - 0.5f) * rx * 0.9f;
                float oz = (WorldLayout.Hash(seed, i + 30, 41) - 0.5f) * rx * 0.9f;
                float oy = (WorldLayout.Hash(seed, i + 40, 59) - 0.5f) * ry * 0.6f;
                float r = rx * (0.62f + WorldLayout.Hash(seed, i + 50, 71) * 0.42f);

                bool deep = WorldLayout.Hash(seed, i + 60, 83) > 0.72f;
                MeshBuilder target = deep ? Bucket(purple, cluster) : Bucket(pink, cluster);

                target.AddSphere(new Vector3(x + ox, canopyY + oy, z + oz),
                    new Vector3(r, ry, r), great ? 5 : 4, great ? 10 : 8);
            }
        }

        // ------------------------------------------------------------------ bamboo

        /// <summary>
        /// Bamboo grows in tight clumps with wide lanes between them, not as a wall. That
        /// is REQ-112's "small obstacles": a clump is something to break line of sight
        /// around, never something that closes a route. The route network itself is paint,
        /// and paint is never Moss, so no clump can ever land on one.
        /// </summary>
        private static void BuildBamboo(Transform group, WorldPalette palette)
        {
            var culms = new MeshBuilder[36];
            var leaves = new MeshBuilder[36];

            for (float x = -44f; x < -14f; x += 2.6f)
            {
                for (float z = -30f; z < 32f; z += 2.6f)
                {
                    float n = WorldLayout.Noise(x, z, 0.12f, 8123);
                    if (n < 0.46f)
                    {
                        continue;
                    }

                    float cx = x + (WorldLayout.Hash(Mathf.RoundToInt(x), Mathf.RoundToInt(z), 3) - 0.5f) * 1.8f;
                    float cz = z + (WorldLayout.Hash(Mathf.RoundToInt(x), Mathf.RoundToInt(z), 5) - 0.5f) * 1.8f;

                    if (!IsPlantable(cx, cz, 1.0f))
                    {
                        continue;
                    }

                    int cluster = ClusterIndex(cx, cz);
                    MeshBuilder culm = Bucket(culms, cluster);
                    MeshBuilder leaf = Bucket(leaves, cluster);

                    int count = 4 + Mathf.RoundToInt(n * 5f);
                    for (int i = 0; i < count; i++)
                    {
                        int seed = Mathf.RoundToInt(cx * 11f + cz * 3f) + i;
                        float a = WorldLayout.Hash(seed, i, 101) * Mathf.PI * 2f;
                        float r = WorldLayout.Hash(seed, i + 7, 103) * 1.05f;
                        float bx = cx + Mathf.Cos(a) * r;
                        float bz = cz + Mathf.Sin(a) * r;
                        float h = 4.6f + WorldLayout.Hash(seed, i + 11, 107) * 2.6f;

                        Quaternion tilt = Quaternion.Euler(
                            (WorldLayout.Hash(seed, i + 13, 109) - 0.5f) * 9f,
                            0f,
                            (WorldLayout.Hash(seed, i + 17, 113) - 0.5f) * 9f);

                        culm.AddCylinder(new Vector3(bx, -0.1f, bz), 0.075f, 0.05f, h, 5, true, tilt);

                        leaf.AddSphere(new Vector3(bx, h * 0.86f, bz),
                            new Vector3(0.68f, 0.95f, 0.68f), 3, 6);
                    }
                }
            }

            FlushClusters(group, culms, "BambooCulms", palette.Bamboo,
                WorldPiece.Solidity.Occluder, SurfaceKind.Foliage);
            FlushClusters(group, leaves, "BambooLeaves", palette.Fern,
                WorldPiece.Solidity.Occluder, SurfaceKind.Foliage);
        }

        // ------------------------------------------------------------------ pine

        private static void BuildGreatPine(Transform group, WorldPalette palette)
        {
            var trunk = new MeshBuilder(512);
            var canopy = new MeshBuilder(1024);

            const float X = -6f;
            const float Z = 5f;
            float y = WorldLayout.DaisY;

            trunk.AddCylinder(new Vector3(X, y - 0.2f, Z), 0.78f, 0.42f, 4.2f, 10,
                true, Quaternion.Euler(7f, 0f, 4f));

            // Three tiers of flat, wide foliage: the classic garden pine silhouette, and
            // the only object in the centre tall enough to be seen from every region.
            float[] tierY = { 4.0f, 5.6f, 7.0f };
            float[] tierR = { 3.1f, 2.5f, 1.6f };

            for (int t = 0; t < tierY.Length; t++)
            {
                float bx = X + (t - 1) * 0.7f;
                float bz = Z + (t == 1 ? 0.8f : -0.4f);

                trunk.AddCylinder(new Vector3(X, y + tierY[t] - 0.6f, Z), 0.2f, 0.12f, 1.1f, 6,
                    true, Quaternion.Euler(58f, t * 130f, 0f));

                canopy.AddSphere(new Vector3(bx, y + tierY[t], bz),
                    new Vector3(tierR[t], 0.55f, tierR[t] * 0.9f), 4, 10);
            }

            WorldPiece.Create(group, "PineTrunk", trunk, palette.Wood,
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
            WorldPiece.Create(group, "PineCanopy", canopy, palette.Pine,
                WorldPiece.Solidity.Occluder, SurfaceKind.Foliage);
        }

        // ------------------------------------------------------------------ scatter

        private static void BuildHedgesAndRocks(Transform group, WorldPalette palette)
        {
            var rocks = new MeshBuilder(2048);
            var hedges = new MeshBuilder(2048);

            for (float x = -43f; x < 43f; x += 2.2f)
            {
                for (float z = -43f; z < 43f; z += 2.2f)
                {
                    float n = WorldLayout.Noise(x, z, 0.17f, 2939);
                    if (n < 0.58f)
                    {
                        continue;
                    }

                    int seed = Mathf.RoundToInt(x * 5f + z * 17f);
                    float px = x + (WorldLayout.Hash(seed, 1, 211) - 0.5f) * 1.8f;
                    float pz = z + (WorldLayout.Hash(seed, 2, 223) - 0.5f) * 1.8f;

                    bool rock = WorldLayout.Hash(seed, 3, 227) > 0.55f;
                    float size = rock ? 0.5f + n * 0.9f : 0.7f + n * 0.7f;

                    if (!IsPlantable(px, pz, size * 0.8f))
                    {
                        continue;
                    }

                    if (rock)
                    {
                        rocks.AddSphere(new Vector3(px, WorldLayout.GroundY + size * 0.32f, pz),
                            new Vector3(size, size * 0.72f, size * 0.86f), 3, 7);
                    }
                    else
                    {
                        // Clipped hedge: a rounded box, kept under a metre so it never
                        // hides a body (REQ-104).
                        hedges.AddSphere(new Vector3(px, WorldLayout.GroundY + 0.34f, pz),
                            new Vector3(size * 1.25f, 0.46f, size * 1.05f), 3, 8);
                    }
                }
            }

            WorldPiece.Create(group, "Rocks", rocks, palette.StoneDarkMat,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "Hedges", hedges, palette.Fern,
                WorldPiece.Solidity.Solid, SurfaceKind.Foliage);
        }

        /// <summary>
        /// Grass tufts and ferns. NO COLLIDER: this is the only category in the world that
        /// is pure decoration, which is exactly why it can be dense. It cannot obstruct
        /// the NavMesh, the aim ray, a bullet or the player, so a thousand of them cost
        /// one draw call and change nothing about the game.
        /// </summary>
        private static void BuildGroundCover(Transform group, WorldPalette palette)
        {
            var tufts = new MeshBuilder(8192);
            int placed = 0;

            for (float x = -43f; x < 43f; x += 1.5f)
            {
                for (float z = -43f; z < 43f; z += 1.5f)
                {
                    if (WorldLayout.Classify(x, z) != GroundSurface.Moss)
                    {
                        continue;
                    }

                    int seed = Mathf.RoundToInt(x * 3f + z * 7f);
                    if (WorldLayout.Hash(seed, 9, 307) < 0.55f)
                    {
                        continue;
                    }

                    float px = x + (WorldLayout.Hash(seed, 1, 311) - 0.5f) * 1.3f;
                    float pz = z + (WorldLayout.Hash(seed, 2, 313) - 0.5f) * 1.3f;
                    float h = 0.22f + WorldLayout.Hash(seed, 3, 317) * 0.3f;

                    for (int b = 0; b < 3; b++)
                    {
                        float a = WorldLayout.Hash(seed, b + 4, 331) * Mathf.PI * 2f;
                        Quaternion tilt = Quaternion.Euler(
                            Mathf.Cos(a) * 26f, a * Mathf.Rad2Deg, Mathf.Sin(a) * 26f);
                        tufts.AddCylinder(new Vector3(px, WorldLayout.GroundY - 0.05f, pz),
                            0.05f, 0.012f, h, 3, false, tilt);
                    }

                    placed++;
                }
            }

            if (placed > 0)
            {
                WorldPiece.Create(group, "GrassAndFerns", tufts, palette.Grass,
                    WorldPiece.Solidity.Visual, SurfaceKind.Default);
            }
        }

        // ------------------------------------------------------------------ lanterns

        /// <summary>
        /// Stone lanterns, placed by walking the route segments and stepping out
        /// perpendicular by the segment's own half width. They therefore line every road
        /// at the correct distance automatically, and a road that gets wider gets lanterns
        /// further apart without anyone editing a list.
        ///
        /// They also do the readability job of REQ-104: a lantern line is a continuous,
        /// evenly spaced cue for where the route goes, which is what lets a player read
        /// the ring at a glance through light distance fog.
        /// </summary>
        private static void BuildLanterns(Transform group, WorldPalette palette)
        {
            var stone = new MeshBuilder(4096);
            const float Spacing = 8.5f;

            for (int i = 0; i < WorldLayout.Paths.Length; i++)
            {
                WorldLayout.PathSegment seg = WorldLayout.Paths[i];
                if (seg.Surface != GroundSurface.Stone)
                {
                    continue;
                }

                Vector2 delta = seg.B - seg.A;
                float length = delta.magnitude;
                if (length < 4f)
                {
                    continue;
                }

                Vector2 dir = delta / length;
                var normal = new Vector2(-dir.y, dir.x);

                for (float t = Spacing * 0.5f; t < length; t += Spacing)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector2 p = seg.A + dir * t + normal * (s * (seg.HalfWidth + 0.85f));

                        if (WorldLayout.IsInPond(p.x, p.y) ||
                            Mathf.Abs(p.x) > 43.5f || Mathf.Abs(p.y) > 43.5f)
                        {
                            continue;
                        }

                        if (s_blockMask != 0 &&
                            Physics.CheckSphere(new Vector3(p.x, 1.0f, p.y), 0.75f, s_blockMask,
                                QueryTriggerInteraction.Ignore))
                        {
                            continue;
                        }

                        bool clear = true;
                        for (int k = 0; k < s_keepOut.Count; k++)
                        {
                            if ((s_keepOut[k] - p).sqrMagnitude < GameplayClearance * GameplayClearance)
                            {
                                clear = false;
                                break;
                            }
                        }

                        if (clear)
                        {
                            AddLantern(stone, p.x, p.y);
                        }
                    }
                }
            }

            WorldPiece.Create(group, "StoneLanterns", stone, palette.Stone,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
        }

        private static void AddLantern(MeshBuilder builder, float x, float z)
        {
            float y = WorldLayout.GroundY;

            builder.AddCylinder(new Vector3(x, y - 0.1f, z), 0.42f, 0.34f, 0.34f, 6);
            builder.AddCylinder(new Vector3(x, y + 0.24f, z), 0.14f, 0.13f, 0.62f, 6);
            builder.AddCylinder(new Vector3(x, y + 0.86f, z), 0.38f, 0.36f, 0.12f, 6);
            builder.AddBox(new Vector3(x, y + 1.19f, z), new Vector3(0.52f, 0.54f, 0.52f));
            builder.AddRoof(new Vector3(x, y + 1.46f, z), 0.92f, 0.92f, 0.3f, 0.18f, 0.1f);
            builder.AddCylinder(new Vector3(x, y + 1.76f, z), 0.09f, 0.02f, 0.2f, 5);
        }
    }
}
