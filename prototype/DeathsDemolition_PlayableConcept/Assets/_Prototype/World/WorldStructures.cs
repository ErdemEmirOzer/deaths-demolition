using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// Everything built by hand rather than grown: the palace, the enclosed west
    /// corridor, the gates, the towers, the pagoda, the courtyard furniture and the wall
    /// that closes the map.
    ///
    /// TWO RULES DECIDE WHAT GOES WHERE
    /// --------------------------------
    /// 1. Anything taller than roughly two metres and wider than roughly two metres is an
    ///    OCCLUDER, because the camera sits 15 m up and 13 m south of the player: from
    ///    that angle a south-facing wall is exactly the thing that hides the fight. Those
    ///    pieces are split into chunks (the perimeter into eight, the corridor into
    ///    three) so only the section actually in the way disappears - a whole wall
    ///    vanishing at once is far more disorienting than the wall itself.
    /// 2. Nothing that reads as a door is a door. The palace is a SOLID shell with
    ///    1.2 m gate alcoves, and the towers and the pagoda have no interior at all. The
    ///    request bans dead ends, and a room with one way in is the purest dead end there
    ///    is; a blockout gains nothing from an interior nobody should enter.
    /// </summary>
    public static class WorldStructures
    {
        // PROTOTYPE-ONLY, all invented: the request fixes the theme, never a dimension.

        private const float PerimeterHeight = 5.2f;
        private const float PerimeterThickness = 1.3f;
        private const float PerimeterInner = 44f;

        private const float CorridorWallTop = 2.9f;
        private const float CorridorWallThickness = 0.4f;

        private const float LowWallHeight = 0.9f;

        public static void Build(Transform root, WorldPalette palette)
        {
            BuildPerimeter(WorldPiece.Group(root, "Perimeter Wall"), palette);
            BuildPalace(WorldPiece.Group(root, "Palace (north landmark)"), palette);
            BuildMainGate(WorldPiece.Group(root, "Main Gate (north landmark)"), palette);
            BuildWatchtower(WorldPiece.Group(root, "Watchtower (north-east landmark)"), palette);
            BuildBellTower(WorldPiece.Group(root, "Bell Tower (north-west landmark)"), palette);
            BuildCorridor(WorldPiece.Group(root, "Enclosed Corridor (west landmark)"), palette);
            BuildPagoda(WorldPiece.Group(root, "Stone Pagoda (south-west landmark)"), palette);
            BuildCourtyard(WorldPiece.Group(root, "Courtyard (centre)"), palette);
            BuildToriiRow(WorldPiece.Group(root, "Torii Row (west radial)"), palette);
            BuildGardenWalls(WorldPiece.Group(root, "Garden Walls (east)"), palette);
            BuildPondShrine(WorldPiece.Group(root, "Pond Shrine (south)"), palette);
        }

        // ------------------------------------------------------------------ perimeter

        /// <summary>
        /// The castle wall. Split in eight because the camera can stand outside the map:
        /// with the player at the south edge the camera sits at z = -54, and the south
        /// wall is then between the two. Fading a quarter of the map's wall is acceptable,
        /// fading all of it is not.
        /// </summary>
        private static void BuildPerimeter(Transform group, WorldPalette palette)
        {
            float inner = PerimeterInner;
            float outer = inner + PerimeterThickness;

            for (int side = 0; side < 4; side++)
            {
                for (int half = 0; half < 2; half++)
                {
                    var builder = new MeshBuilder(256);

                    float a = half == 0 ? -outer : 0f;
                    float b = half == 0 ? 0f : outer;

                    switch (side)
                    {
                        case 0: AddWallRun(builder, a, inner, b, outer); break;          // north
                        case 1: AddWallRun(builder, a, -outer, b, -inner); break;        // south
                        case 2: AddWallRun(builder, inner, a, outer, b); break;          // east
                        default: AddWallRun(builder, -outer, a, -inner, b); break;       // west
                    }

                    string name = "Wall_" + "NSEW"[side] + (half == 0 ? "a" : "b");
                    WorldPiece.Create(group, name, builder, palette.Stone,
                        WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
                }
            }

            // Corner turrets: they break the box silhouette and tell you which corner of
            // the map you are in without reading a minimap.
            var turrets = new MeshBuilder(512);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -1f : 1f;
                float sz = (i < 2) ? 1f : -1f;
                Vector3 c = new Vector3(sx * (inner + 0.4f), 0f, sz * (inner + 0.4f));

                turrets.AddSlab(c.x - 2.6f, c.z - 2.6f, c.x + 2.6f, c.z + 2.6f, 0f, PerimeterHeight + 0.6f);
                turrets.AddRoof(new Vector3(c.x, PerimeterHeight + 0.6f, c.z), 6.6f, 6.6f, 1.7f, 0.2f, 0.4f);
            }

            WorldPiece.Create(group, "CornerTurrets", turrets, palette.Tile,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
        }

        private static void AddWallRun(MeshBuilder builder, float x0, float z0, float x1, float z1)
        {
            // Battered base plus a lighter upper course: two boxes read as masonry, one
            // box reads as a crate.
            float shrink = 0.25f;
            builder.AddSlab(x0, z0, x1, z1, 0f, 2.1f);
            builder.AddSlab(x0 + shrink, z0 + shrink, x1 - shrink, z1 - shrink, 2.1f, PerimeterHeight);
            builder.AddSlab(x0 - 0.15f, z0 - 0.15f, x1 + 0.15f, z1 + 0.15f,
                PerimeterHeight, PerimeterHeight + 0.35f);
        }

        // ------------------------------------------------------------------ palace

        private static void BuildPalace(Transform group, WorldPalette palette)
        {
            float hx = WorldLayout.PalaceHalfX;
            float zs = WorldLayout.PalaceSouthZ;
            float zn = WorldLayout.PalaceNorthZ;
            float wallTop = WorldLayout.PalaceWallY;

            var stone = new MeshBuilder(1024);
            var wood = new MeshBuilder(2048);
            var plaster = new MeshBuilder(1024);
            var roof = new MeshBuilder(1024);

            // Stone podium the whole building stands on. 0.38 and not 0.45: the podium
            // sticks out past the veranda at both corners, and at 0.45 that lip is above
            // the agent's 0.4 m climb, so the NavMesh would leave a knee-high ledge you
            // can see onto but no enemy can follow you across.
            const float PodiumY = 0.38f;
            stone.AddSlab(-hx - 1.2f, zs - 1.2f, hx + 1.2f, zn + 1.2f, -0.2f, PodiumY);

            // Solid shell. No interior: see the class summary.
            plaster.AddSlab(-hx, zs, hx, zn, PodiumY, wallTop);

            // Timber frame drawn onto the shell, so it reads as a building and not a box.
            for (float x = -hx + 2f; x <= hx - 2f; x += 4f)
            {
                wood.AddSlab(x - 0.18f, zs - 0.12f, x + 0.18f, zs + 0.12f, PodiumY, wallTop);
                wood.AddSlab(x - 0.18f, zn - 0.12f, x + 0.18f, zn + 0.12f, PodiumY, wallTop);
            }

            wood.AddSlab(-hx - 0.15f, zs - 0.15f, hx + 0.15f, zs + 0.15f, wallTop - 0.5f, wallTop);
            wood.AddSlab(-hx - 0.15f, zn - 0.15f, hx + 0.15f, zn + 0.15f, wallTop - 0.5f, wallTop);

            // The two gates: alcoves, not doors.
            for (int s = -1; s <= 1; s += 2)
            {
                float gx = s * 8f;
                plaster.AddSlab(gx - 2.1f, zs - 0.1f, gx + 2.1f, zs + 1.2f, 3.4f, 3.7f);
                wood.AddSlab(gx - 2.3f, zs - 0.25f, gx - 1.9f, zs + 1.3f, PodiumY, 3.7f);
                wood.AddSlab(gx + 1.9f, zs - 0.25f, gx + 2.3f, zs + 1.3f, PodiumY, 3.7f);
                wood.AddSlab(gx - 2.4f, zs - 0.35f, gx + 2.4f, zs - 0.05f, 3.7f, 4.1f);

                // Stone steps up to the veranda. One 0.35 m rise, split in two treads so
                // it reads as a stair rather than as a kerb.
                stone.AddSlab(gx - 2.6f, zs - 3.2f, gx + 2.6f, zs - 2.6f, 0f, 0.17f);
                stone.AddSlab(gx - 2.6f, zs - 2.6f, gx + 2.6f, zs - 2.0f, 0f, WorldLayout.VerandaY);
            }

            // Vermilion door panels, sunk into the alcoves. Accent use 1 of the budget.
            var accent = new MeshBuilder(128);
            for (int s = -1; s <= 1; s += 2)
            {
                float gx = s * 8f;
                accent.AddSlab(gx - 1.9f, zs + 0.85f, gx + 1.9f, zs + 1.15f, PodiumY, 3.4f);
            }

            // Veranda: a raised timber deck the whole front runs along.
            float vz0 = zs - 2f;
            wood.AddSlab(-16f, vz0, 16f, zs, 0f, WorldLayout.VerandaY);

            for (float x = -15f; x <= 15f; x += 3f)
            {
                wood.AddSlab(x - 0.16f, vz0 + 0.2f, x + 0.16f, vz0 + 0.52f, WorldLayout.VerandaY, 4.0f);
            }

            roof.AddRoof(new Vector3(0f, 4.0f, vz0 + 1.1f), 34f, 4.6f, 0.85f, 0.55f, 0.3f);
            roof.AddRoof(new Vector3(0f, wallTop, (zs + zn) * 0.5f), 2f * hx + 6f, (zn - zs) + 6f,
                3.4f, 0.22f, 0.55f);

            WorldPiece.Create(group, "PalacePodium", stone, palette.Stone,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "PalaceWalls", plaster, palette.Plaster,
                WorldPiece.Solidity.Occluder, SurfaceKind.Paper);
            WorldPiece.Create(group, "PalaceTimber", wood, palette.Wood,
                WorldPiece.Solidity.Occluder, SurfaceKind.Wood);
            WorldPiece.Create(group, "PalaceRoof", roof, palette.Tile,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
            WorldPiece.Create(group, "PalaceDoors", accent, palette.UseAccent(),
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
        }

        // ------------------------------------------------------------------ gates

        /// <summary>
        /// The romon straddling the 16 m palace road at z = 22. It is a sightline pinch,
        /// not a barrier: the posts stand 11 m apart on a 16 m road, so you always see
        /// through it and never have to path around it.
        /// </summary>
        private static void BuildMainGate(Transform group, WorldPalette palette)
        {
            var wood = new MeshBuilder(512);
            var roof = new MeshBuilder(256);
            var accent = new MeshBuilder(256);
            var stone = new MeshBuilder(256);

            const float Z = 22f;
            const float PostX = 5.5f;

            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * PostX;
                stone.AddSlab(x - 0.9f, Z - 0.9f, x + 0.9f, Z + 0.9f, -0.1f, 0.45f);
                accent.AddCylinder(new Vector3(x, 0.45f, Z), 0.55f, 0.48f, 4.6f, 10);
                wood.AddSlab(x - 1.4f, Z - 0.35f, x + 1.4f, Z + 0.35f, 3.3f, 3.65f);
            }

            wood.AddSlab(-PostX - 1.6f, Z - 0.4f, PostX + 1.6f, Z + 0.4f, 4.55f, 5.05f);
            wood.AddSlab(-PostX - 0.9f, Z - 0.3f, PostX + 0.9f, Z + 0.3f, 4.05f, 4.4f);
            roof.AddRoof(new Vector3(0f, 5.05f, Z), 2f * PostX + 5.5f, 3.8f, 1.35f, 0.3f, 0.4f);

            WorldPiece.Create(group, "GateBases", stone, palette.Stone,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "GatePosts", accent, palette.UseAccent(),
                WorldPiece.Solidity.Occluder, SurfaceKind.Wood);
            WorldPiece.Create(group, "GateBeams", wood, palette.Wood,
                WorldPiece.Solidity.Occluder, SurfaceKind.Wood);
            WorldPiece.Create(group, "GateRoof", roof, palette.Tile,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
        }

        /// <summary>
        /// Four torii marking the west radial. They straddle a 6 m stone path with 4.4 m
        /// of clearance, so like the main gate they frame the route without narrowing it.
        /// One mesh, one accent use.
        /// </summary>
        private static void BuildToriiRow(Transform group, WorldPalette palette)
        {
            var accent = new MeshBuilder(512);

            for (float x = -16f; x >= -29f; x -= 4.2f)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    accent.AddCylinder(new Vector3(x, 0f, s * 2.2f), 0.22f, 0.19f, 3.3f, 8);
                }

                accent.AddSlab(x - 0.3f, -2.9f, x + 0.3f, 2.9f, 3.3f, 3.62f);
                accent.AddSlab(x - 0.22f, -2.5f, x + 0.22f, 2.5f, 2.75f, 3.0f);
            }

            WorldPiece.Create(group, "Torii", accent, palette.UseAccent(),
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
        }

        // ------------------------------------------------------------------ towers

        private static void BuildWatchtower(Transform group, WorldPalette palette)
        {
            var stone = new MeshBuilder(256);
            var wood = new MeshBuilder(512);
            var roof = new MeshBuilder(256);

            const float X = 27f;
            const float Z = 28f;

            stone.AddSlab(X - 3.4f, Z - 3.4f, X + 3.4f, Z + 3.4f, -0.1f, 2.6f);
            wood.AddSlab(X - 2.9f, Z - 2.9f, X + 2.9f, Z + 2.9f, 2.6f, 6.4f);
            wood.AddSlab(X - 3.5f, Z - 3.5f, X + 3.5f, Z + 3.5f, 4.5f, 4.8f);
            roof.AddRoof(new Vector3(X, 6.4f, Z), 8.6f, 8.6f, 2.4f, 0.22f, 0.5f);

            WorldPiece.Create(group, "TowerBase", stone, palette.Stone,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
            WorldPiece.Create(group, "TowerBody", wood, palette.Plaster,
                WorldPiece.Solidity.Occluder, SurfaceKind.Paper);
            WorldPiece.Create(group, "TowerRoof", roof, palette.Tile,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);

            // THE ISLAND UNDER THE TOWER.
            // Every piece here is a SHELL, not a solid: AddSlab writes six faces and no
            // interior. The tower base stands ON the forecourt, so the voxeliser still
            // finds the forecourt's own ground plane inside the shell, sees 2.7 m of
            // headroom under the lid at y 2.6 - more than the 2 m agent - and bakes a
            // perfectly walkable 6.8 x 6.8 m floor that is sealed on all four sides.
            //
            // That island is why the map's self-check reported
            //     'NE_StonePathCorner' has no complete path to the player start (PathPartial)
            // for three runs: the spawn point sat on it, SamplePosition found it 0.18 m
            // away, and no path out of it exists or ever could.
            //
            // The point has moved (WorldMarkers), but the island is the actual defect and
            // it is carved out here, at the geometry that causes it. Nothing else on this
            // map encloses ground: the palace and the pagoda have no interior by rule 2
            // above, and the corridor has a doorway.
            CarveNavMesh(group, "TowerInterior (NavMesh carve)",
                new Vector3(X, 0.75f, Z), new Vector3(6.8f, 3.5f, 6.8f));
        }

        /// <summary>
        /// Punches a not-walkable hole in the NavMesh over a box.
        ///
        /// Ordering is load-bearing and safe: <see cref="WorldBuilder"/> runs structures
        /// before <see cref="WorldNavigation"/>, so every volume created here is already
        /// registered by the time the surface bakes. The LAYER is load-bearing too -
        /// NavMeshSurface skips any modifier whose layer is outside its layerMask, and
        /// this surface collects Environment and CameraOccluder only.
        /// </summary>
        private static void CarveNavMesh(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;

            int environment = DD.Prototype.Core.PrototypeLayers.Environment;
            if (environment >= 0)
            {
                go.layer = environment;
            }

            var volume = go.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
            volume.center = Vector3.zero;
            volume.size = size;

            // Area 1 is Unity's reserved "Not Walkable" and outranks every other area.
            volume.area = 1;
        }

        private static void BuildBellTower(Transform group, WorldPalette palette)
        {
            var wood = new MeshBuilder(512);
            var roof = new MeshBuilder(256);
            var stone = new MeshBuilder(256);

            const float X = -31f;
            const float Z = 30f;

            stone.AddSlab(X - 2.6f, Z - 2.6f, X + 2.6f, Z + 2.6f, -0.1f, 0.4f);

            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -1f : 1f;
                float sz = (i < 2) ? 1f : -1f;
                wood.AddCylinder(new Vector3(X + sx * 1.9f, 0.4f, Z + sz * 1.9f), 0.3f, 0.26f, 4.4f, 8);
            }

            wood.AddSlab(X - 2.3f, Z - 2.3f, X + 2.3f, Z + 2.3f, 4.8f, 5.2f);
            wood.AddCylinder(new Vector3(X, 2.6f, Z), 0.75f, 0.62f, 1.5f, 10);
            roof.AddRoof(new Vector3(X, 5.2f, Z), 6.4f, 6.4f, 1.9f, 0.25f, 0.45f);

            WorldPiece.Create(group, "BellBase", stone, palette.Stone,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "BellFrame", wood, palette.Wood,
                WorldPiece.Solidity.Occluder, SurfaceKind.Wood);
            WorldPiece.Create(group, "BellRoof", roof, palette.Tile,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
        }

        private static void BuildPagoda(Transform group, WorldPalette palette)
        {
            var stone = new MeshBuilder(512);

            const float X = -31f;
            const float Z = -32f;

            float y = 0f;
            float w = 3.2f;

            stone.AddSlab(X - 2.4f, Z - 2.4f, X + 2.4f, Z + 2.4f, -0.15f, 0.5f);
            y = 0.5f;

            for (int tier = 0; tier < 5; tier++)
            {
                float bodyH = 1.5f - tier * 0.12f;
                stone.AddSlab(X - w * 0.35f, Z - w * 0.35f, X + w * 0.35f, Z + w * 0.35f, y, y + bodyH);
                stone.AddRoof(new Vector3(X, y + bodyH, Z), w, w, 0.42f, 0.5f, 0.22f);

                y += bodyH + 0.42f;
                w -= 0.42f;
            }

            stone.AddCylinder(new Vector3(X, y, Z), 0.18f, 0.05f, 1.5f, 6);

            WorldPiece.Create(group, "Pagoda", stone, palette.Stone,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
        }

        // ------------------------------------------------------------------ corridor

        /// <summary>
        /// REQ-111's enclosed corridor. Three links, not the required two: the south
        /// mouth at z = -12, the north mouth at z = +12, and a doorway in the east wall at
        /// z = 0 where the west radial arrives. A fourth way past it exists outside as the
        /// bamboo bypass, so a player who refuses to enter the corridor is never stuck.
        ///
        /// Cut into three chunks along its length: standing at the south mouth should fade
        /// the south roof and leave the north end drawn, otherwise the whole west of the
        /// map blinks at once.
        /// </summary>
        private static void BuildCorridor(Transform group, WorldPalette palette)
        {
            float wx = WorldLayout.CorridorWestX;
            float ex = WorldLayout.CorridorEastX;
            float zs = WorldLayout.CorridorSouthZ;
            float zn = WorldLayout.CorridorNorthZ;
            float door = WorldLayout.CorridorDoorHalf;

            const int Chunks = 3;
            float span = (zn - zs) / Chunks;

            for (int c = 0; c < Chunks; c++)
            {
                float z0 = zs + c * span;
                float z1 = z0 + span;

                var wood = new MeshBuilder(512);
                var roof = new MeshBuilder(512);

                // West wall: solid for the whole run.
                wood.AddSlab(wx, z0, wx + CorridorWallThickness, z1, 0f, CorridorWallTop);

                // East wall: solid except the doorway at z = 0.
                AddSplitWall(wood, ex - CorridorWallThickness, ex, z0, z1, -door, door, CorridorWallTop);

                // Posts and the low sill you can shoot over from outside.
                for (float z = z0 + 1f; z < z1; z += 2.5f)
                {
                    wood.AddSlab(wx, z - 0.16f, wx + 0.5f, z + 0.16f, 0f, CorridorWallTop);
                    wood.AddSlab(ex - 0.5f, z - 0.16f, ex, z + 0.16f, 0f, CorridorWallTop);
                }

                roof.AddSlab(wx - 0.55f, z0, ex + 0.55f, z1, CorridorWallTop, CorridorWallTop + 0.22f);
                roof.AddSlab(wx - 0.2f, z0, ex + 0.2f, z1,
                    CorridorWallTop + 0.22f, WorldLayout.CorridorRoofY + 0.1f);

                string suffix = c == 0 ? "South" : (c == 1 ? "Mid" : "North");
                WorldPiece.Create(group, "CorridorFrame_" + suffix, wood, palette.Wood,
                    WorldPiece.Solidity.Occluder, SurfaceKind.Wood);
                WorldPiece.Create(group, "CorridorRoof_" + suffix, roof, palette.Tile,
                    WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
            }
        }

        /// <summary>Wall along z with a gap between <paramref name="gap0"/> and <paramref name="gap1"/>.</summary>
        private static void AddSplitWall(MeshBuilder builder, float x0, float x1,
            float z0, float z1, float gap0, float gap1, float top)
        {
            if (gap1 <= z0 || gap0 >= z1)
            {
                builder.AddSlab(x0, z0, x1, z1, 0f, top);
                return;
            }

            if (gap0 > z0)
            {
                builder.AddSlab(x0, z0, x1, gap0, 0f, top);
            }

            if (gap1 < z1)
            {
                builder.AddSlab(x0, gap1, x1, z1, 0f, top);
            }

            // Lintel over the opening, so the doorway reads as a doorway.
            builder.AddSlab(x0, Mathf.Max(gap0, z0), x1, Mathf.Min(gap1, z1), 2.35f, top);
        }

        // ------------------------------------------------------------------ courtyard

        private static void BuildCourtyard(Transform group, WorldPalette palette)
        {
            var stone = new MeshBuilder(1024);
            var rake = new MeshBuilder(2048);
            var wood = new MeshBuilder(512);

            // The dais the Great Pine stands on. 0.3 m: under the agent climb, so the
            // NavMesh walks straight onto it and enemies do not orbit it stupidly.
            stone.AddSlab(-9.5f, 1.5f, -2.5f, 8.5f, 0f, WorldLayout.DaisY);
            stone.AddSlab(-9.9f, 1.1f, -2.1f, 8.9f, 0f, WorldLayout.DaisY - 0.14f);

            // Water basin opposite it, so the courtyard is asymmetric and you can tell
            // which way you are facing inside it.
            stone.AddCylinder(new Vector3(6f, 0f, -5f), 1.5f, 1.35f, 0.55f, 10);
            stone.AddCylinder(new Vector3(6f, 0.55f, -5f), 1.15f, 1.15f, 0.06f, 10);

            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -1f : 1f;
                float sz = (i < 2) ? 1f : -1f;
                wood.AddCylinder(new Vector3(6f + sx * 1.7f, 0f, -5f + sz * 1.7f), 0.12f, 0.1f, 2.3f, 6);
            }

            wood.AddRoof(new Vector3(6f, 2.3f, -5f), 4.6f, 4.6f, 0.8f, 0.3f, 0.25f);

            // Raked gravel. Pure silhouette work: from a 15 m camera these lines tell you
            // instantly that the centre is a courtyard and not just a clearing.
            for (float z = -WorldLayout.CourtyardHalf; z <= WorldLayout.CourtyardHalf; z += 0.9f)
            {
                float runStart = float.NaN;

                for (float x = -WorldLayout.CourtyardHalf; x <= WorldLayout.CourtyardHalf; x += 0.5f)
                {
                    bool inside = WorldLayout.IsInCourtyard(x, z) &&
                                  !(x > -10.4f && x < -1.6f && z > 0.6f && z < 9.4f) &&
                                  !(x > 4f && x < 8f && z > -7f && z < -3f);

                    if (inside && float.IsNaN(runStart))
                    {
                        runStart = x;
                    }
                    else if (!inside && !float.IsNaN(runStart))
                    {
                        rake.AddSlab(runStart, z - 0.07f, x, z + 0.07f, 0.012f, 0.055f);
                        runStart = float.NaN;
                    }
                }

                if (!float.IsNaN(runStart))
                {
                    rake.AddSlab(runStart, z - 0.07f, WorldLayout.CourtyardHalf, z + 0.07f, 0.012f, 0.055f);
                }
            }

            WorldPiece.Create(group, "CourtyardStone", stone, palette.Stone,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "BasinRoof", wood, palette.Wood,
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
            WorldPiece.Create(group, "RakedGravel", rake, palette.Gravel,
                WorldPiece.Solidity.Visual, SurfaceKind.Default);
        }

        // ------------------------------------------------------------------ garden

        /// <summary>
        /// REQ-109's low garden walls: 0.9 m, which is high enough to stop a body and low
        /// enough that you always see the enemy behind it. They never form a closed shape,
        /// so they shape movement without ever creating a pocket.
        /// </summary>
        private static void BuildGardenWalls(Transform group, WorldPalette palette)
        {
            var wall = new MeshBuilder(1024);
            var cap = new MeshBuilder(1024);

            var runs = new[]
            {
                new Vector4(19f, 12f, 30f, 18f),
                new Vector4(20f, -12f, 31f, -18f),
                new Vector4(38f, 8f, 43f, 16f),
                new Vector4(37f, -16f, 43f, -21f),
                new Vector4(16f, 20f, 22f, 14f),
                new Vector4(28f, -28f, 34f, -33f)
            };

            for (int i = 0; i < runs.Length; i++)
            {
                Vector4 r = runs[i];
                Vector2 a = new Vector2(r.x, r.y);
                Vector2 b = new Vector2(r.z, r.w);
                Vector2 d = (b - a).normalized;
                float len = Vector2.Distance(a, b);
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                Vector2 mid = (a + b) * 0.5f;

                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                wall.AddBox(new Vector3(mid.x, LowWallHeight * 0.5f, mid.y),
                    new Vector3(0.45f, LowWallHeight, len), rot);
                cap.AddBox(new Vector3(mid.x, LowWallHeight + 0.08f, mid.y),
                    new Vector3(0.72f, 0.16f, len), rot);
            }

            WorldPiece.Create(group, "GardenWalls", wall, palette.Plaster,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
            WorldPiece.Create(group, "GardenWallCaps", cap, palette.Tile,
                WorldPiece.Solidity.Solid, SurfaceKind.Stone);
        }

        private static void BuildPondShrine(Transform group, WorldPalette palette)
        {
            var wood = new MeshBuilder(256);
            var accent = new MeshBuilder(256);
            var roof = new MeshBuilder(128);

            const float X = -19f;
            const float Z = -22f;

            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -1f : 1f;
                float sz = (i < 2) ? 1f : -1f;
                wood.AddCylinder(new Vector3(X + sx * 1.1f, 0f, Z + sz * 1.1f), 0.16f, 0.14f, 2.1f, 6);
            }

            accent.AddSlab(X - 1.4f, Z - 1.4f, X + 1.4f, Z + 1.4f, 2.1f, 2.35f);
            roof.AddRoof(new Vector3(X, 2.35f, Z), 4.2f, 4.2f, 1.2f, 0.28f, 0.3f);

            WorldPiece.Create(group, "ShrinePosts", wood, palette.Wood,
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
            WorldPiece.Create(group, "ShrineBeam", accent, palette.UseAccent(),
                WorldPiece.Solidity.Solid, SurfaceKind.Wood);
            WorldPiece.Create(group, "ShrineRoof", roof, palette.Tile,
                WorldPiece.Solidity.Occluder, SurfaceKind.Stone);
        }
    }
}
