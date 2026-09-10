using DD.Prototype.Feedback;
using Unity.AI.Navigation;
using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// Everything you stand on, fall into or cross: the painted ground, the koi pond and
    /// the narrow bridge.
    ///
    /// HOW THE GROUND IS PAINTED
    /// -------------------------
    /// <see cref="WorldLayout.Classify"/> is sampled at the centre of every 1 m cell in
    /// the 90x90 grid, and equal cells on the same row are merged into ONE quad
    /// (run-length, west to east). That matters: 8100 cells would be 8100 quads and
    /// 32 400 vertices of flat ground, while the merged result is a few hundred quads for
    /// exactly the same silhouette. The seam between rows is shared-edge coplanar, so
    /// there is no crack and no z-fight.
    ///
    /// One mesh per surface type, so each gets its own material AND its own
    /// <see cref="SurfaceTag"/>: a bullet hitting the gravel courtyard and a bullet
    /// hitting the stone road already sound different, for free, with no per-object work.
    ///
    /// WHY THE POND IS A HOLE AND NOT A SURFACE
    /// ---------------------------------------
    /// The pond has to be a real obstacle or REQ-110's "narrow, risky bridge" is just
    /// decoration - you would simply walk around it through the water. So the ground
    /// generates NO cells inside the pond, the basin and the water are marked
    /// <c>ignoreFromBuild</c> for the NavMesh, and a low stone coping with an invisible
    /// extension keeps bodies out. The result is a genuine hole in the NavMesh with two
    /// authored ways past it: the bridge, and the shore road that parallels it.
    /// </summary>
    public static class WorldGround
    {
        /// <summary>PROTOTYPE-ONLY, invented: height of the invisible pond barrier.</summary>
        private const float PondBarrierHeight = 1.25f;

        /// <summary>PROTOTYPE-ONLY, invented: visible stone coping around the pond.</summary>
        private const float PondCopingHeight = 0.42f;

        private const float PondCopingWidth = 0.5f;

        /// <summary>PROTOTYPE-ONLY, invented: bridge railing height and post spacing.</summary>
        private const float RailHeight = 0.62f;

        private const float RailSpacing = 2.4f;

        public static void Build(Transform root, WorldPalette palette)
        {
            Transform group = WorldPiece.Group(root, "Ground");

            PaintGround(group, palette);
            BuildPond(WorldPiece.Group(root, "Koi Pond"), palette);
            BuildBridge(WorldPiece.Group(root, "Bridge"), palette);
            BuildPuddles(group, palette);
        }

        // ------------------------------------------------------------------ ground

        private static void PaintGround(Transform group, WorldPalette palette)
        {
            int cells = Mathf.RoundToInt(WorldLayout.MapSize / WorldLayout.CellSize);

            // One builder per surface. Index by the enum value.
            var builders = new MeshBuilder[5];
            for (int i = 0; i < builders.Length; i++)
            {
                builders[i] = new MeshBuilder(4096);
            }

            for (int j = 0; j < cells; j++)
            {
                float z0 = -WorldLayout.HalfMap + j * WorldLayout.CellSize;
                float z1 = z0 + WorldLayout.CellSize;
                float zc = z0 + WorldLayout.CellSize * 0.5f;

                int runStart = 0;
                GroundSurface runSurface = SampleRow(0, zc);

                for (int i = 1; i <= cells; i++)
                {
                    GroundSurface here = i < cells ? SampleRow(i, zc) : GroundSurface.None;

                    if (here == runSurface)
                    {
                        continue;
                    }

                    EmitRun(builders, runSurface, runStart, i, z0, z1);
                    runStart = i;
                    runSurface = here;
                }
            }

            for (int i = 1; i < builders.Length; i++)
            {
                var surface = (GroundSurface)i;
                WorldPiece.Create(group, "Ground_" + surface, builders[i],
                    palette.ForSurface(surface), WorldPiece.Solidity.Solid,
                    WorldPalette.ToSurfaceKind(surface));
            }
        }

        private static GroundSurface SampleRow(int i, float zc)
        {
            float xc = -WorldLayout.HalfMap + i * WorldLayout.CellSize + WorldLayout.CellSize * 0.5f;
            return WorldLayout.Classify(xc, zc);
        }

        private static void EmitRun(MeshBuilder[] builders, GroundSurface surface,
            int from, int to, float z0, float z1)
        {
            if (surface == GroundSurface.None || to <= from)
            {
                return;
            }

            float x0 = -WorldLayout.HalfMap + from * WorldLayout.CellSize;
            float x1 = -WorldLayout.HalfMap + to * WorldLayout.CellSize;

            builders[(int)surface].AddQuad(
                new Vector3(x0, WorldLayout.GroundY, z0),
                new Vector3(x0, WorldLayout.GroundY, z1),
                new Vector3(x1, WorldLayout.GroundY, z1),
                new Vector3(x1, WorldLayout.GroundY, z0),
                Vector3.up, 0.25f);
        }

        /// <summary>
        /// Dark, glossy patches on the stone. This is the whole of "after the rain"
        /// (REQ-103) in a blockout: no texture, just a wetter material sitting 1.5 cm
        /// over the road wherever the noise says water pooled. No collider, so they are
        /// invisible to physics, aim and the NavMesh.
        /// </summary>
        private static void BuildPuddles(Transform group, WorldPalette palette)
        {
            var builder = new MeshBuilder(512);
            int placed = 0;

            for (float x = -WorldLayout.HalfMap + 2f; x < WorldLayout.HalfMap - 2f; x += 3f)
            {
                for (float z = -WorldLayout.HalfMap + 2f; z < WorldLayout.HalfMap - 2f; z += 3f)
                {
                    float n = WorldLayout.Noise(x, z, 0.11f, 7717);
                    if (n < 0.72f)
                    {
                        continue;
                    }

                    float jx = x + (WorldLayout.Hash(Mathf.RoundToInt(x), Mathf.RoundToInt(z), 31) - 0.5f) * 2.4f;
                    float jz = z + (WorldLayout.Hash(Mathf.RoundToInt(x), Mathf.RoundToInt(z), 57) - 0.5f) * 2.4f;

                    GroundSurface surface = WorldLayout.Classify(jx, jz);
                    if (surface != GroundSurface.Stone && surface != GroundSurface.Gravel)
                    {
                        continue;
                    }

                    float r = 0.7f + n * 1.6f;
                    builder.AddSphere(new Vector3(jx, WorldLayout.GroundY + 0.015f, jz),
                        new Vector3(r, 0.004f, r * 0.75f), 2, 8);
                    placed++;
                }
            }

            if (placed > 0)
            {
                WorldPiece.Create(group, "WetPatches", builder, palette.StoneWetMat,
                    WorldPiece.Solidity.Visual, SurfaceKind.Default);
            }
        }

        // ------------------------------------------------------------------ pond

        private static void BuildPond(Transform group, WorldPalette palette)
        {
            var basin = new MeshBuilder(2048);
            var water = new MeshBuilder(2048);
            var coping = new MeshBuilder(2048);
            var barrier = new MeshBuilder(2048);

            int cells = Mathf.RoundToInt(WorldLayout.MapSize / WorldLayout.CellSize);
            float c = WorldLayout.CellSize;

            for (int j = 0; j < cells; j++)
            {
                float z0 = -WorldLayout.HalfMap + j * c;
                float z1 = z0 + c;
                float zc = z0 + c * 0.5f;

                for (int i = 0; i < cells; i++)
                {
                    float x0 = -WorldLayout.HalfMap + i * c;
                    float x1 = x0 + c;
                    float xc = x0 + c * 0.5f;

                    if (!WorldLayout.IsInPond(xc, zc))
                    {
                        continue;
                    }

                    // Basin floor, slightly dished so the shallows read at the edge.
                    basin.AddQuad(
                        new Vector3(x0, WorldLayout.PondFloorY, z0),
                        new Vector3(x0, WorldLayout.PondFloorY, z1),
                        new Vector3(x1, WorldLayout.PondFloorY, z1),
                        new Vector3(x1, WorldLayout.PondFloorY, z0),
                        Vector3.up, 0.25f);

                    water.AddQuad(
                        new Vector3(x0, WorldLayout.WaterY, z0),
                        new Vector3(x0, WorldLayout.WaterY, z1),
                        new Vector3(x1, WorldLayout.WaterY, z1),
                        new Vector3(x1, WorldLayout.WaterY, z0),
                        Vector3.up, 0.2f);

                    // Shore walls and coping wherever the neighbour is not water.
                    EdgeIfShore(basin, coping, barrier, xc, zc, xc - c, zc, x0, z0, x0, z1);
                    EdgeIfShore(basin, coping, barrier, xc, zc, xc + c, zc, x1, z1, x1, z0);
                    EdgeIfShore(basin, coping, barrier, xc, zc, xc, zc - c, x1, z0, x0, z0);
                    EdgeIfShore(basin, coping, barrier, xc, zc, xc, zc + c, x0, z1, x1, z1);
                }
            }

            WorldPiece.Create(group, "PondBasin", basin, palette.StoneDarkMat,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "PondWater", water, palette.Water,
                WorldPiece.Solidity.Visual, SurfaceKind.Water);
            WorldPiece.Create(group, "PondCoping", coping, palette.Stone,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "PondBarrier", barrier, null,
                WorldPiece.Solidity.Invisible, SurfaceKind.Stone);

            BuildKoi(group, palette);

            // The pond must never contribute walkable surface. Marking the whole group
            // ignored is stronger than relying on the barrier: even the basin floor,
            // 1.1 m down and perfectly flat, cannot produce a NavMesh island now.
            // NavMeshSurface filters its markups by layer mask BEFORE it looks at them
            // (CollectSources: "if ((m_LayerMask & (1 << m.gameObject.layer)) == 0)
            // continue"). This grouping object is created on the Default layer, so
            // without this line the modifier is silently discarded and the pond floor
            // bakes as a walkable island 1.1 m down.
            int env = DD.Prototype.Core.PrototypeLayers.Environment;
            if (env >= 0)
            {
                group.gameObject.layer = env;
            }

            var modifier = group.gameObject.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;
            modifier.applyToChildren = true;
        }

        private static void EdgeIfShore(MeshBuilder basin, MeshBuilder coping, MeshBuilder barrier,
            float xc, float zc, float nx, float nz,
            float ax, float az, float bx, float bz)
        {
            if (WorldLayout.IsInPond(nx, nz))
            {
                return;
            }

            // Inner wall of the basin, water level down to the floor.
            basin.AddQuad(
                new Vector3(ax, WorldLayout.PondFloorY, az),
                new Vector3(ax, WorldLayout.GroundY, az),
                new Vector3(bx, WorldLayout.GroundY, bz),
                new Vector3(bx, WorldLayout.PondFloorY, bz),
                0.3f);

            // Visible coping: a stone lip you can read as "edge" from the camera.
            float ox = Mathf.Sign(nx - xc) * PondCopingWidth;
            float oz = Mathf.Sign(nz - zc) * PondCopingWidth;

            coping.AddSlab(
                Mathf.Min(ax, bx + ox), Mathf.Min(az, bz + oz),
                Mathf.Max(ax, bx + ox), Mathf.Max(az, bz + oz),
                WorldLayout.GroundY - 0.05f, WorldLayout.GroundY + PondCopingHeight);

            // Invisible extension, skipped where the bridge crosses so the deck is clear.
            float mx = (ax + bx) * 0.5f;
            float mz = (az + bz) * 0.5f;
            if (Mathf.Abs(mx - WorldLayout.BridgeX) <= WorldLayout.BridgeWidth * 0.5f + 0.6f)
            {
                return;
            }

            barrier.AddSlab(
                Mathf.Min(ax, bx + ox * 0.4f), Mathf.Min(az, bz + oz * 0.4f),
                Mathf.Max(ax, bx + ox * 0.4f), Mathf.Max(az, bz + oz * 0.4f),
                WorldLayout.GroundY, WorldLayout.GroundY + PondBarrierHeight);
        }

        /// <summary>Koi. Three ellipsoids under the surface: pure REQ-103 flavour.</summary>
        private static void BuildKoi(Transform group, WorldPalette palette)
        {
            var builder = new MeshBuilder(256);

            for (int i = 0; i < 9; i++)
            {
                float x = -8f + WorldLayout.Hash(i, 3, 991) * 18f;
                float z = -34f + WorldLayout.Hash(i, 7, 173) * 12f;

                if (!WorldLayout.IsInPond(x, z))
                {
                    continue;
                }

                float yaw = WorldLayout.Hash(i, 11, 55) * 360f;
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 centre = new Vector3(x, WorldLayout.WaterY - 0.18f, z);

                builder.AddBox(centre, new Vector3(0.22f, 0.14f, 0.62f), rot);
                builder.AddBox(centre + rot * new Vector3(0f, 0f, -0.42f),
                    new Vector3(0.05f, 0.16f, 0.28f), rot);
            }

            WorldPiece.Create(group, "Koi", builder, palette.Koi,
                WorldPiece.Solidity.Visual, SurfaceKind.Default);
        }

        // ------------------------------------------------------------------ bridge

        /// <summary>
        /// REQ-110's choke. Three metres wide, arched, railed, with piers in the water.
        /// The deck is generated as a strip that samples
        /// <see cref="WorldLayout.BridgeHeight"/>, which is the same function the spawn
        /// markers and the Special Weapon points use, so nothing ever floats over it or
        /// sinks into it.
        /// </summary>
        private static void BuildBridge(Transform group, WorldPalette palette)
        {
            var deck = new MeshBuilder(1024);
            var rails = new MeshBuilder(1024);
            var piers = new MeshBuilder(512);

            float half = WorldLayout.BridgeWidth * 0.5f;
            float x = WorldLayout.BridgeX;
            const float Step = 0.5f;
            const float Thickness = 0.22f;

            for (float z = WorldLayout.BridgeSouthZ; z < WorldLayout.BridgeNorthZ; z += Step)
            {
                float z2 = Mathf.Min(z + Step, WorldLayout.BridgeNorthZ);
                float y0 = WorldLayout.BridgeHeight(z);
                float y1 = WorldLayout.BridgeHeight(z2);

                // Walking surface.
                deck.AddQuad(
                    new Vector3(x - half, y0, z),
                    new Vector3(x - half, y1, z2),
                    new Vector3(x + half, y1, z2),
                    new Vector3(x + half, y0, z),
                    Vector3.up, 0.4f);

                // Underside, so the arch is not paper thin when seen from the shore road.
                deck.AddQuad(
                    new Vector3(x + half, y0 - Thickness, z),
                    new Vector3(x + half, y1 - Thickness, z2),
                    new Vector3(x - half, y1 - Thickness, z2),
                    new Vector3(x - half, y0 - Thickness, z),
                    Vector3.down, 0.4f);

                // Beams.
                deck.AddQuad(
                    new Vector3(x - half, y0 - Thickness, z),
                    new Vector3(x - half, y1 - Thickness, z2),
                    new Vector3(x - half, y1, z2),
                    new Vector3(x - half, y0, z), 0.4f);

                deck.AddQuad(
                    new Vector3(x + half, y0, z),
                    new Vector3(x + half, y1, z2),
                    new Vector3(x + half, y1 - Thickness, z2),
                    new Vector3(x + half, y0 - Thickness, z), 0.4f);

                // Continuous handrail.
                rails.AddSlab(x - half - 0.06f, z, x - half + 0.06f, z2,
                    y0 + RailHeight - 0.1f, y1 + RailHeight);
                rails.AddSlab(x + half - 0.06f, z, x + half + 0.06f, z2,
                    y0 + RailHeight - 0.1f, y1 + RailHeight);
            }

            for (float z = WorldLayout.BridgeSouthZ + 1f; z < WorldLayout.BridgeNorthZ; z += RailSpacing)
            {
                float y = WorldLayout.BridgeHeight(z);
                rails.AddBox(new Vector3(x - half, y + RailHeight * 0.5f, z),
                    new Vector3(0.12f, RailHeight, 0.12f));
                rails.AddBox(new Vector3(x + half, y + RailHeight * 0.5f, z),
                    new Vector3(0.12f, RailHeight, 0.12f));
            }

            for (float z = WorldLayout.BridgeSouthZ + 3f; z < WorldLayout.BridgeNorthZ - 2f; z += 5f)
            {
                if (!WorldLayout.IsInPond(x, z))
                {
                    continue;
                }

                float y = WorldLayout.BridgeHeight(z) - Thickness;
                piers.AddCylinder(new Vector3(x - half + 0.4f, WorldLayout.PondFloorY, z),
                    0.22f, 0.18f, y - WorldLayout.PondFloorY, 6);
                piers.AddCylinder(new Vector3(x + half - 0.4f, WorldLayout.PondFloorY, z),
                    0.22f, 0.18f, y - WorldLayout.PondFloorY, 6);
            }

            WorldPiece.Create(group, "BridgeDeck", deck, palette.WoodMid,
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
            WorldPiece.Create(group, "BridgeRails", rails, palette.Wood,
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
            WorldPiece.Create(group, "BridgePiers", piers, palette.Wood,
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
        }
    }
}
