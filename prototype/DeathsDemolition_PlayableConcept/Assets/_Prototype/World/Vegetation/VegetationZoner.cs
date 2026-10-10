using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · Semantic grid → per-species densities (LOOK-BIBLE §1 recipes from <see cref="VegetationRecipe"/>).
    /// Distance fields (nearest-seed propagation, two passes) give every cell its distance to paths, plaza, water, walls,
    /// buildings, rocks, bamboo and any obstacle; the zone is chosen by priority (court, karesansui, bamboo, shore,
    /// amaochi, wall band, building band, wall outer, pond bank, bridge approach, grove, bamboo edge, lawn), then the
    /// path / plaza transitions, the corridor caps (principle 2, R1), the clear radii and S-05's multipliers apply.
    /// Accents are a deterministic post-pass — AD v1.2 (10.10) tsuyu garden: the v1 higanbana strips, ajisai clusters in
    /// three colours, hanashōbu shore strips and kurinsō shade clusters (see <see cref="Accents"/>). Load time only.
    /// </summary>
    public static class VegetationZoner
    {
        private const int SaltMacro = 11, SaltMid = 12, SaltMoss = 13, SaltLeaf = 14, SaltPathJitter = 15, SaltWallRun = 16, SaltBamboo = 17, SaltAccent = 18;

        /// <summary>Per-cell distances and nearest seeds (exposed for tests / debug views).</summary>
        public sealed class Distances
        {
            public int[] Path, Plaza, Water, Wall, Building, Rock, Obstacle, Bamboo, NonBamboo;
        }

        public static VegetationDensityField Evaluate(VegetationGrid grid, VegetationRecipe recipe)
        {
            return Evaluate(grid, recipe, out _);
        }

        public static VegetationDensityField Evaluate(VegetationGrid grid, VegetationRecipe recipe, out Distances dist)
        {
            if (recipe == null) recipe = VegetationRecipe.Load();
            int n = grid.Count, w = grid.Width, h = grid.Height;
            VegetationRecipe.Transitions tr = recipe.transitions;
            var field = new VegetationDensityField(w, h, grid.Cell, grid.Origin, recipe) { DefaultGroundY = grid.DefaultGroundY };

            // ---- seeds and distance fields
            var seed = new bool[n];
            dist = new Distances();
            dist.Path = Field(grid, seed, i => grid.Has(i, VegetationGrid.Flag.Path));
            dist.Plaza = Field(grid, seed, i => grid.Has(i, VegetationGrid.Flag.Plaza));
            dist.Water = Field(grid, seed, i => grid.Has(i, VegetationGrid.Flag.Water));
            dist.Wall = Field(grid, seed, i => grid.Obstacle[i] == (byte)VegetationObstacle.Wall && grid.ObstacleHeight[i] >= tr.wallMinHeight);
            dist.Building = Field(grid, seed, i => grid.Obstacle[i] == (byte)VegetationObstacle.Building || grid.Has(i, VegetationGrid.Flag.Roofed));
            dist.Rock = Field(grid, seed, i => grid.Obstacle[i] == (byte)VegetationObstacle.Rock);
            dist.Obstacle = Field(grid, seed, i =>
            {
                var o = (VegetationObstacle)grid.Obstacle[i];
                return (VegetationGrid.IsSolid(o) && o != VegetationObstacle.Kerb) || grid.Has(i, VegetationGrid.Flag.Water) || grid.Has(i, VegetationGrid.Flag.Roofed);
            });
            dist.Bamboo = Field(grid, seed, i => grid.Has(i, VegetationGrid.Flag.Bamboo));
            dist.NonBamboo = Field(grid, seed, i => !grid.Has(i, VegetationGrid.Flag.Bamboo));

            // ---- trees rasterised: nearest grove crown (normalised r), litter centre (drifted downwind)
            var groveR = new float[n];
            var groveTree = new int[n];
            var litterR = new float[n];
            for (int i = 0; i < n; i++) { groveR[i] = float.MaxValue; litterR[i] = float.MaxValue; groveTree[i] = -1; }
            float driftRad = tr.litterDriftDegrees * Mathf.Deg2Rad;
            var drift = new Vector2(Mathf.Cos(driftRad), Mathf.Sin(driftRad));
            for (int t = 0; t < grid.Trees.Count; t++)
            {
                VegetationGrid.Tree tree = grid.Trees[t];
                float R = Mathf.Max(0.5f, tree.Radius);
                if (tree.Grove)
                {
                    float reach = R * tr.groveOuter;
                    int ti = t;
                    VegetationWorldProbe.ForCellsInRect(grid, tree.Position.x - reach, tree.Position.y - reach, tree.Position.x + reach, tree.Position.y + reach, i =>
                    {
                        float r = (grid.CellCenter(i) - tree.Position).magnitude / R;
                        if (r < groveR[i]) { groveR[i] = r; groveTree[i] = ti; }
                    });
                }

                if (tree.Litter)
                {
                    Vector2 centre = tree.Position + drift * (tr.litterDownwind * R);
                    float reach = R * tr.litterOuter;
                    VegetationWorldProbe.ForCellsInRect(grid, centre.x - reach, centre.y - reach, centre.x + reach, centre.y + reach, i =>
                    {
                        float r = (grid.CellCenter(i) - centre).magnitude / R;
                        if (r < litterR[i]) litterR[i] = r;
                    });
                }
            }

            // ---- clear points rasterised (bit 1 spawn, 2 weapon, 4 door)
            var clear = new byte[n];
            for (int k = 0; k < grid.ClearPoints.Count; k++)
            {
                VegetationGrid.ClearPoint cp = grid.ClearPoints[k];
                byte bit = cp.Kind == VegetationGrid.ClearKind.Spawn ? (byte)1 : cp.Kind == VegetationGrid.ClearKind.Weapon ? (byte)2 : (byte)4;
                float r = cp.Radius;
                VegetationWorldProbe.ForCellsInRect(grid, cp.Position.x - r, cp.Position.y - r, cp.Position.x + r, cp.Position.y + r, i =>
                {
                    if ((grid.CellCenter(i) - cp.Position).sqrMagnitude <= r * r) clear[i] |= bit;
                });
            }

            // ---- per cell
            var d = new float[VegetationTypes.Count];
            var zoneOf = new byte[n];
            var corridorOf = new bool[n];
            Vector2 shadowDir = tr.shadowDirection.sqrMagnitude > 1e-6f ? tr.shadowDirection.normalized : new Vector2(0.38f, -0.92f);
            float half = grid.Cell * 0.5f;
            for (int i = 0; i < n; i++)
            {
                field.GroundY[i] = float.IsNaN(grid.GroundY[i]) ? grid.DefaultGroundY : grid.GroundY[i];
                // 08.10 (S-05): mask zone 13 = plaza joint tufts — the one zone that grows ON the plaza.
                bool joint = grid.Zone[i] == (byte)VegetationZone.PlazaJoint && JointGrowable(grid, i);
                if (!grid.Growable(i) && !joint) continue;
                Vector2 p = grid.CellCenter(i);
                float dPath = Edge(grid, i, dist.Path, half);
                float dPlaza = Edge(grid, i, dist.Plaza, half);
                float dWater = Edge(grid, i, dist.Water, half);
                float dWall = Edge(grid, i, dist.Wall, half);
                float dBuilding = Edge(grid, i, dist.Building, half);
                float dRock = Edge(grid, i, dist.Rock, half);
                float dObstacle = Edge(grid, i, dist.Obstacle, half);
                float dBamboo = Edge(grid, i, dist.Bamboo, half);
                float dInsideBamboo = Edge(grid, i, dist.NonBamboo, half);
                float pathJitter = (VegetationHash.ValueNoise(p.x, p.y, tr.pathEdgeJitterWavelength, SaltPathJitter) * 2f - 1f) * tr.pathEdgeJitter;
                float dPathJ = Mathf.Max(0f, dPath + pathJitter);
                bool bamboo = grid.Has(i, VegetationGrid.Flag.Bamboo);

                for (int t = 0; t < d.Length; t++) d[t] = 0f;
                float hScale = 1f, hCap = 0f;
                var forced = (VegetationZone)grid.Zone[i];
                VegetationZone zone;

                if (forced != VegetationZone.Auto)
                {
                    zone = forced;
                }
                else if (grid.Has(i, VegetationGrid.Flag.MossCourt))
                {
                    zone = VegetationZone.MossCourt;
                }
                else if (InRects(recipe.regions.karesansui, p))
                {
                    zone = dRock <= 0.45f ? VegetationZone.KaresansuiCollar : VegetationZone.Karesansui;
                }
                else if (bamboo)
                {
                    zone = dPath < tr.bambooPathSide ? VegetationZone.BambooPathSide : VegetationZone.Bamboo;
                }
                else if (dWater < tr.pondShore)
                {
                    zone = VegetationZone.PondShore;
                }
                else if (dBuilding < tr.buildingAmaochi)
                {
                    zone = VegetationZone.BareSoil;   // amaochi gravel drip strip
                }
                else if (InWallBand(grid, i, dist.Wall, dWall, tr, out bool wallShadow, shadowDir))
                {
                    zone = wallShadow ? VegetationZone.WallBaseShadow : VegetationZone.WallBase;
                }
                else if (dBuilding < tr.buildingBand)
                {
                    zone = ShadowFace(grid, i, dist.Building, shadowDir, tr.shadowFaceDot) ? VegetationZone.BuildingShadow : VegetationZone.BuildingSurround;
                }
                else if (dWall < tr.wallOuter)
                {
                    zone = VegetationZone.WallOuter;
                }
                else if (dWater < tr.pondBank)
                {
                    zone = VegetationZone.PondBank;
                }
                else if (InApproach(recipe.regions.bridgeApproaches, p))
                {
                    zone = VegetationZone.BridgeApproach;
                }
                else if (groveTree[i] >= 0 && groveR[i] < tr.groveInner)
                {
                    zone = VegetationZone.Grove;
                }
                else if (groveTree[i] >= 0 && groveR[i] < tr.groveOuter)
                {
                    zone = VegetationZone.GroveSkirt;
                }
                else if (dBamboo < tr.bambooEdgeBand + (VegetationHash.ValueNoise(p.x, p.y, tr.bambooEdgeJitter, SaltBamboo) - 0.5f) * tr.bambooEdgeJitter)
                {
                    zone = VegetationZone.BambooEdge;
                }
                else
                {
                    zone = VegetationZone.Lawn;
                }

                if (zone == VegetationZone.PathEdge) dPathJ = 0f;   // forced Z5 = the inner band

                VegetationRecipe.ZoneRecipe zr = recipe.Zone(zone);
                for (int t = 0; t < d.Length && t < zr.density.Length; t++) d[t] = zr.density[t];
                hScale = zr.heightScale;
                hCap = zr.heightCap;

                switch (zone)
                {
                    case VegetationZone.Lawn:
                        Lawn(d, p, tr);
                        break;
                    case VegetationZone.Grove:
                        // FE only on the shadow (GGD) side of the trunk (a mask-forced grove without a tree keeps its FE).
                        if (groveTree[i] >= 0)
                        {
                            Vector2 fromTrunk = p - grid.Trees[groveTree[i]].Position;
                            if (fromTrunk.sqrMagnitude > 1e-4f && Vector2.Dot(fromTrunk.normalized, shadowDir) < tr.shadowFaceDot) d[(int)VegetationType.Fern] = 0f;
                        }

                        break;
                    case VegetationZone.Bamboo:
                        hCap = dInsideBamboo >= tr.bambooTallInside ? 0f : recipe.Zone(VegetationZone.BambooEdge).heightCap;
                        break;
                    case VegetationZone.PondBank:
                        // Moisture gradient: GM share grows toward the water.
                        d[(int)VegetationType.GrassMid] *= Mathf.Lerp(1.4f, 0.8f, Mathf.InverseLerp(tr.pondShore, tr.pondBank, dWater));
                        break;
                    case VegetationZone.WallOuter:
                        Lawn(d, p, tr);
                        break;
                }

                // ---- Z9 plaza transition
                if (zone != VegetationZone.PlazaJoint && dPlaza < tr.plazaNoTall)
                {
                    if (dPlaza < tr.plazaMoss)
                    {
                        Zero(d);
                        d[(int)VegetationType.Moss] = recipe.Zone(VegetationZone.PlazaEdge).density[(int)VegetationType.Moss];
                    }
                    else if (dPlaza < tr.plazaRamp)
                    {
                        float k = Mathf.Lerp(tr.plazaRampStartFraction, 1f, Mathf.InverseLerp(tr.plazaMoss, tr.plazaRamp, dPlaza));
                        d[(int)VegetationType.GrassShort] *= k;
                        KeepOnly(d, VegetationType.GrassShort, VegetationType.Moss, VegetationType.LeafLitter);
                    }
                    else if (dPlaza < tr.plazaMidStart)
                    {
                        d[(int)VegetationType.GrassMid] = 0f;
                    }

                    d[(int)VegetationType.GrassTall] = 0f;
                }

                // ---- Z5 path transition (bamboo lanes have their own sides)
                if (zone != VegetationZone.PlazaJoint && !bamboo && dPathJ < tr.pathOuter)
                {
                    if (dPathJ < tr.pathInner)
                    {
                        VegetationRecipe.ZoneRecipe pe = recipe.Zone(VegetationZone.PathEdge);
                        for (int t = 0; t < d.Length && t < pe.density.Length; t++) d[t] = pe.density[t];
                        hScale = Mathf.Min(hScale, pe.heightScale > 0f ? pe.heightScale : tr.pathInnerHeight);
                    }
                    else
                    {
                        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tr.pathInner, tr.pathOuter, dPathJ));
                        d[(int)VegetationType.GrassShort] *= Mathf.Lerp(tr.pathInnerShortFraction, 1f, k);
                        hScale *= Mathf.Lerp(tr.pathRampHeightStart, 1f, k);
                        if (dPathJ < tr.pathMidStart) d[(int)VegetationType.GrassMid] = 0f;
                        d[(int)VegetationType.GrassTall] = 0f;
                        d[(int)VegetationType.Kumazasa] = 0f;
                        d[(int)VegetationType.Hydrangea] = 0f;
                    }
                }

                // ---- momiji litter (ML), units/m²
                if (litterR[i] < tr.litterOuter)
                {
                    float k = litterR[i] < tr.litterInner ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tr.litterInner, tr.litterOuter, litterR[i]));
                    d[(int)VegetationType.LeafLitter] = Mathf.Max(d[(int)VegetationType.LeafLitter], tr.litterLeavesPerM2 * k);
                }

                // ---- corridor (principle 2, R1)
                bool corridor;
                if (grid.Corridor[i] == 2) corridor = true;
                else if (grid.Corridor[i] == 1) corridor = false;
                else corridor = !bamboo && dObstacle > tr.corridorObstacleDistance && (zone == VegetationZone.Lawn || zone == VegetationZone.Grove || zone == VegetationZone.GroveSkirt || zone == VegetationZone.WallOuter || zone == VegetationZone.BridgeApproach || zone == VegetationZone.PathEdge);
                if (corridor)
                {
                    d[(int)VegetationType.GrassTall] = 0f;
                    d[(int)VegetationType.Fern] = 0f;
                    d[(int)VegetationType.Kumazasa] = 0f;
                    d[(int)VegetationType.Hydrangea] = 0f;
                    d[(int)VegetationType.SpiderLily] = 0f;
                    d[(int)VegetationType.GrassMid] = Mathf.Min(d[(int)VegetationType.GrassMid], tr.corridorMidDensityCap);
                    d[(int)VegetationType.LeafLitter] = Mathf.Min(d[(int)VegetationType.LeafLitter], tr.corridorLitterCap);
                    hCap = Cap(hCap, tr.corridorHeightCap);
                }

                // ---- clear radii
                byte c = clear[i];
                if ((c & 1) != 0)
                {
                    float ss = d[(int)VegetationType.GrassShort] * tr.spawnShortScale;
                    Zero(d);
                    d[(int)VegetationType.GrassShort] = ss;
                }

                if ((c & 2) != 0) hCap = Cap(hCap, tr.weaponHeightCap);
                if ((c & 4) != 0)
                {
                    KeepOnly(d, VegetationType.GrassShort, VegetationType.Moss, VegetationType.LeafLitter);
                    hCap = Cap(hCap, tr.doorHeightCap);
                }

                // ---- S-05 multipliers, then write (units → instances)
                float dm = VegetationGrid.Multiplier(grid.DensityMul[i]);
                hScale *= VegetationGrid.Multiplier(grid.HeightMul[i]);
                for (int t = 0; t < d.Length; t++)
                {
                    float units = d[t] * dm;
                    if (units <= 0f) continue;
                    field.SetDensity(t, i, units / Mathf.Max(1f, recipe.Get((VegetationType)t).unitsPerInstance));
                }

                field.SetHeight(i, hScale, hCap);
                zoneOf[i] = (byte)zone;
                corridorOf[i] = corridor;
            }

            Accents(grid, recipe, field, dist, zoneOf, corridorOf, clear, shadowDir, groveR, groveTree);
            Zone7CorridorMoss.Apply(grid, recipe, field, zoneOf, corridorOf, clear);
#if UNITY_EDITOR
            VegetationPreview.RecordDiagnostics(grid, field, zoneOf, corridorOf, clear);
#endif
            return field;
        }

        // ------------------------------------------------------------------ zone helpers

        /// <summary>A zone-13 cell on the real plaza may retain the paving's Path flag; all safety exclusions still apply.</summary>
        private static bool JointGrowable(VegetationGrid grid, int i)
        {
            bool plazaJoint = grid.Zone[i] == (byte)VegetationZone.PlazaJoint && grid.Has(i, VegetationGrid.Flag.Plaza);
            if (!plazaJoint) return false;
            const ushort blocked = (ushort)(VegetationGrid.Flag.Water | VegetationGrid.Flag.Excluded | VegetationGrid.Flag.Roofed);
            if ((grid.Flags[i] & (ushort)VegetationGrid.Flag.Ground) == 0 || (grid.Flags[i] & blocked) != 0) return false;
            return !VegetationGrid.IsSolid((VegetationObstacle)grid.Obstacle[i]);
        }

        private static void Lawn(float[] d, Vector2 p, VegetationRecipe.Transitions tr)
        {
            // SS ±25 % over 10 m; GM in 1.5–3 m patches covering ~15 %; MO / LC in small clusters (same averages).
            d[(int)VegetationType.GrassShort] *= 1f + tr.macroNoiseAmount * (VegetationHash.ValueNoise(p.x, p.y, tr.macroNoiseWavelength, SaltMacro) * 2f - 1f);
            d[(int)VegetationType.GrassMid] = Cluster(d[(int)VegetationType.GrassMid], p, tr.midPatchWavelength, tr.midPatchCoverage, SaltMid);
            d[(int)VegetationType.Moss] = Cluster(d[(int)VegetationType.Moss], p, tr.mossClusterWavelength, tr.mossClusterCoverage, SaltMoss);
            d[(int)VegetationType.LeafClump] = Cluster(d[(int)VegetationType.LeafClump], p, tr.leafClusterWavelength, tr.leafClusterCoverage, SaltLeaf);
            float inPatch = d[(int)VegetationType.GrassMid] > 0f ? 0.6f : 1f;   // SS thins inside GM patches
            d[(int)VegetationType.GrassShort] *= inPatch;
        }

        /// <summary>Average density <paramref name="avg"/> concentrated into noise patches covering <paramref name="coverage"/> of the area.</summary>
        private static float Cluster(float avg, Vector2 p, float wavelength, float coverage, int salt)
        {
            if (avg <= 0f || coverage <= 0f) return 0f;
            // Value noise is roughly bell-shaped around 0.5; 1 - coverage·0.55 is a calibrated threshold for the top 'coverage' share.
            float threshold = 1f - Mathf.Clamp01(coverage) * 0.55f - 0.2f;
            return VegetationHash.ValueNoise(p.x, p.y, wavelength, salt) > threshold ? avg / Mathf.Max(0.02f, coverage) : 0f;
        }

        private static bool InWallBand(VegetationGrid grid, int i, int[] wallSeed, float dWall, VegetationRecipe.Transitions tr, out bool shadow, Vector2 shadowDir)
        {
            shadow = false;
            int s = wallSeed[i];
            if (s < 0) return false;
            float height = grid.ObstacleHeight[s];
            float band = height < tr.lowWallHeight ? tr.wallBandLow : tr.wallBand;
            if (dWall >= band) return false;
            // Broken runs (3–7 m parts, 1–2 m gaps): noise along the wall at the seed.
            Vector2 sp = grid.CellCenter(s);
            if (VegetationHash.ValueNoise(sp.x, sp.y, tr.wallRunWavelength, SaltWallRun) < tr.wallRunThreshold) return false;
            shadow = ShadowFace(grid, i, wallSeed, shadowDir, tr.shadowFaceDot);
            return true;
        }

        /// <summary>The cell lies on the shadow side of its nearest seed (the seed's face toward the camera, bible: south faces are in shadow).</summary>
        private static bool ShadowFace(VegetationGrid grid, int i, int[] seed, Vector2 shadowDir, float dot)
        {
            int s = seed[i];
            if (s < 0) return false;
            Vector2 v = grid.CellCenter(i) - grid.CellCenter(s);
            return v.sqrMagnitude > 1e-6f && Vector2.Dot(v.normalized, shadowDir) > dot;
        }

        private static bool InRects(Vector4[] rects, Vector2 p)
        {
            if (rects == null) return false;
            for (int r = 0; r < rects.Length; r++)
            {
                Vector4 q = rects[r];
                if (p.x >= q.x && p.x <= q.z && p.y >= q.y && p.y <= q.w) return true;
            }

            return false;
        }

        private static bool InApproach(Vector3[] points, Vector2 p)
        {
            if (points == null) return false;
            for (int k = 0; k < points.Length; k++)
            {
                Vector3 a = points[k];
                if ((new Vector2(a.x, a.y) - p).sqrMagnitude <= a.z * a.z) return true;
            }

            return false;
        }

        private static void Zero(float[] d)
        {
            for (int t = 0; t < d.Length; t++) d[t] = 0f;
        }

        private static void KeepOnly(float[] d, VegetationType a, VegetationType b, VegetationType c)
        {
            for (int t = 0; t < d.Length; t++)
            {
                if (t != (int)a && t != (int)b && t != (int)c) d[t] = 0f;
            }
        }

        private static float Cap(float current, float cap) => current <= 0f ? cap : Mathf.Min(current, cap);

        private static float Edge(VegetationGrid grid, int i, int[] nearest, float half)
        {
            int s = nearest[i];
            if (s < 0) return float.MaxValue;
            if (s == i) return 0f;
            int dx = i % grid.Width - s % grid.Width, dz = i / grid.Width - s / grid.Width;
            return Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) * grid.Cell - half);
        }

        // ------------------------------------------------------------------ distance fields

        private static int[] Field(VegetationGrid grid, bool[] seed, System.Func<int, bool> isSeed)
        {
            for (int i = 0; i < seed.Length; i++) seed[i] = isSeed(i);
            var nearest = new int[seed.Length];
            NearestSeeds(seed, grid.Width, grid.Height, nearest);
            return nearest;
        }

        /// <summary>Approximate Euclidean nearest-seed transform: forward and backward 8-neighbour propagation of seed indices.</summary>
        public static void NearestSeeds(bool[] seed, int w, int h, int[] nearest)
        {
            int n = w * h;
            for (int i = 0; i < n; i++) nearest[i] = seed[i] ? i : -1;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    Relax(nearest, w, h, i, x, z, x - 1, z);
                    Relax(nearest, w, h, i, x, z, x - 1, z - 1);
                    Relax(nearest, w, h, i, x, z, x, z - 1);
                    Relax(nearest, w, h, i, x, z, x + 1, z - 1);
                }

                for (int x = w - 1; x >= 0; x--) Relax(nearest, w, h, z * w + x, x, z, x + 1, z);
            }

            for (int z = h - 1; z >= 0; z--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = z * w + x;
                    Relax(nearest, w, h, i, x, z, x + 1, z);
                    Relax(nearest, w, h, i, x, z, x + 1, z + 1);
                    Relax(nearest, w, h, i, x, z, x, z + 1);
                    Relax(nearest, w, h, i, x, z, x - 1, z + 1);
                }

                for (int x = 0; x < w; x++) Relax(nearest, w, h, z * w + x, x, z, x - 1, z);
            }
        }

        private static void Relax(int[] nearest, int w, int h, int i, int x, int z, int nx, int nz)
        {
            if (nx < 0 || nz < 0 || nx >= w || nz >= h) return;
            int cand = nearest[nz * w + nx];
            if (cand < 0) return;
            int cur = nearest[i];
            if (cur < 0 || D2(cand, x, z, w) < D2(cur, x, z, w)) nearest[i] = cand;
        }

        private static int D2(int s, int x, int z, int w)
        {
            int dx = s % w - x, dz = s / w - z;
            return dx * dx + dz * dz;
        }

        // ------------------------------------------------------------------ accents (AD v1.2, 10.10: tsuyu garden)

        private const int SaltHydrangea = 41, SaltIris = 51, SaltPrimrose = 61;
        private const byte KindHiganbana = 0, KindHydrangea = 1, KindIris = 2, KindPrimrose = 3;
        private const float GroundShare = 0.6f;   // an accent needs this share of its footprint on flower ground

        /// <summary>One placed accent as a capsule (a disc when A == B); the gap rules measure edge to edge.</summary>
        private struct Capsule
        {
            public Vector2 A, B;
            public float R;
            public byte Kind;
            public int Group;
        }

        private sealed class HydrangeaCluster
        {
            public Vector2 Centre;
            public float Radius;
            public int Cell;
            public int Group;
            public bool Shade;
            public int Colour;
            public List<int> Plants;
        }

        /// <summary>What the accent pass of the last <see cref="Evaluate"/> placed (diagnostics; plants are cells at High).</summary>
        public struct AccentCounts
        {
            public int HiganbanaStrips;
            public int HydrangeaGroups, HydrangeaClusters, HydrangeaPlants, HydrangeaA, HydrangeaB, HydrangeaC;
            public int IrisStrips, IrisPlants;
            public int PrimroseClusters, PrimrosePlants;

            public override string ToString()
            {
                return "higanbana strips " + HiganbanaStrips + " | ajisai groups " + HydrangeaGroups + " clusters " + HydrangeaClusters + " (A " + HydrangeaA + " B " + HydrangeaB + " C " + HydrangeaC
                       + ") plants " + HydrangeaPlants + " | hanashōbu strips " + IrisStrips + " plants " + IrisPlants + " | kurinsō clusters " + PrimroseClusters + " plants " + PrimrosePlants;
            }
        }

        public static AccentCounts LastAccents { get; private set; }

        /// <remarks>
        /// AD v1.2 (10.10, user note "more purple / pink / lilac sprinkled in"), from the kit that exists — no new species:
        /// 1. Higanbana (SL variant 0, the atlas red): the v1 strips, unchanged — their candidates, salts and sizes; their
        ///    density keeps the v1 layer clamp (6/m²) now that the SL layer scale is 16/m². No hint: variant 0.
        /// 2. Ajisai (HY): clusters Ø 1.2–2.0 m of 2–4 plants in asymmetric groups of 1–3 (satellites at uneven gaps and a
        ///    100–150° turn), group anchors ≥ 6 m from every cluster (hash order on a 1 m lattice = blue noise), 48 at most
        ///    (AD 40–55). Sites: pond bank, Z7 building band corners, wall-base shadow, grove skirt, courtyard corners, the
        ///    moss-court rim (cells there take a 0.6 m cap: the court's 0.15 m would delete them). One colour per cluster:
        ///    C 25 % by hash, then A 40 % to the wettest / shadiest, B 35 % the rest (sun).
        /// 3. Hanashōbu (SL variants 1 / 2): strips 1.5–4 × 0.4–0.6 m along the shore, 0.5–1.2 m from the water, 5–8 plants,
        ///    10–16 strips, ≥ 3 m apart, ≥ 4 m from a higanbana strip; colour A / B by hash.
        /// 4. Kurinsō (SL variant 3): Ø 0.8–1.2 m, 5–8 plants with 3–4 fuki (LC) leaves around, 12–20 clusters ≥ 5 m apart, on
        ///    shaded edges (the sun-side pond bank whose slope faces away from the sun, wall-base / building shadow, bamboo
        ///    edge, the grove skirt's shadow side).
        /// Every new accent stands on flower ground (<see cref="FlowerGround"/>) and keeps a 1 m edge gap to a different
        /// accent, so no cell carries two colours. Plants are written one per 0.25 m cell (exact counts at High); the colour
        /// variant rides in <see cref="VegetationDensityField.VariantHint"/> (<see cref="VegetationPlacer.WithAccentVariant"/>),
        /// never on a corridor cell.
        /// </remarks>
        private static void Accents(VegetationGrid grid, VegetationRecipe recipe, VegetationDensityField field, Distances dist, byte[] zoneOf, bool[] corridorOf, byte[] clear,
            Vector2 shadowDir, float[] groveR, int[] groveTree)
        {
            VegetationRecipe.Accents ac = recipe.accents;
            VegetationRecipe.Transitions tr = recipe.transitions;
            float half = grid.Cell * 0.5f;
            int stride = Mathf.Max(1, Mathf.RoundToInt(1f / grid.Cell));
            var placed = new List<Capsule>(128);
            var counts = new AccentCounts();

            // ---- 1. higanbana strips (v1): inside the bible's regions, along path edges or the pond bank
            var slCandidates = new List<int>(256);
            Vector4[] regions = ac.spiderLilyRegions;
            for (int z = 0; z < grid.Height; z += stride)
            {
                for (int x = 0; x < grid.Width; x += stride)
                {
                    int i = grid.Index(x, z);
                    if (!grid.Growable(i) || clear[i] != 0 || !InRects(regions, grid.CellCenter(i))) continue;
                    float dPath = Edge(grid, i, dist.Path, half);
                    bool pathBand = dPath >= tr.pathInner && dPath <= tr.pathOuter + 0.3f;
                    if (pathBand || (VegetationZone)zoneOf[i] == VegetationZone.PondBank) slCandidates.Add(i);
                }
            }

            SortByHash(slCandidates, SaltAccent + 2);
            int target = ac.spiderLilyStripsMin + (int)(VegetationHash.Cell01(7, 7, SaltAccent + 3) * (ac.spiderLilyStripsMax - ac.spiderLilyStripsMin + 1));
            var strips = new List<Vector2>(16);
            int sl = (int)VegetationType.SpiderLily;
            for (int k = 0; k < slCandidates.Count && strips.Count < target; k++)
            {
                int i = slCandidates[k];
                Vector2 p = grid.CellCenter(i);
                if (TooClose(strips, p, ac.spiderLilySpacing)) continue;
                strips.Add(p);
                // Along the edge: perpendicular to the nearest path (or water) seed.
                int s = dist.Path[i] >= 0 ? dist.Path[i] : dist.Water[i];
                Vector2 normal = s >= 0 ? grid.CellCenter(i) - grid.CellCenter(s) : Vector2.right;
                if (normal.sqrMagnitude < 1e-6f) normal = Vector2.right;
                normal.Normalize();
                var along = new Vector2(-normal.y, normal.x);
                float length = Mathf.Lerp(ac.spiderLilyLengthMin, ac.spiderLilyLengthMax, VegetationHash.Cell01(i, 1, SaltAccent + 4));
                int count = ac.spiderLilyPerStripMin + (int)(VegetationHash.Cell01(i, 2, SaltAccent + 4) * (ac.spiderLilyPerStripMax - ac.spiderLilyPerStripMin + 1));
                float width = Mathf.Max(grid.Cell, ac.spiderLilyWidth);
                float density = Mathf.Min(count / (length * width), ac.spiderLilyStripDensityCap);
                float reach = length * 0.5f + width;
                VegetationWorldProbe.ForCellsInRect(grid, p.x - reach, p.y - reach, p.x + reach, p.y + reach, j =>
                {
                    if (!grid.Growable(j)) return;
                    Vector2 v = grid.CellCenter(j) - p;
                    if (Mathf.Abs(Vector2.Dot(v, along)) > length * 0.5f || Mathf.Abs(Vector2.Dot(v, normal)) > width * 0.5f) return;
                    field.SetDensity(sl, j, density);
                });
                placed.Add(new Capsule { A = p - along * (length * 0.5f), B = p + along * (length * 0.5f), R = width * 0.5f, Kind = KindHiganbana, Group = -1 });
            }

            counts.HiganbanaStrips = strips.Count;

            // ---- flower ground (AD v1.2 exclusions) and the two distance fields the new accents need
            bool[] ground = FlowerGround(grid, recipe, dist, zoneOf, corridorOf, clear, groveR);
            int n = grid.Count;
            var corridorSeed = new int[n];
            NearestSeeds(corridorOf, grid.Width, grid.Height, corridorSeed);
            int[] courtSeed = null;
            var outsideCourt = new bool[n];
            bool anyCourt = false;
            for (int i = 0; i < n; i++)
            {
                bool court = zoneOf[i] == (byte)VegetationZone.MossCourt;
                outsideCourt[i] = !court;
                anyCourt |= court;
            }

            if (anyCourt)
            {
                courtSeed = new int[n];
                NearestSeeds(outsideCourt, grid.Width, grid.Height, courtSeed);
            }

            float cellArea = grid.Cell * grid.Cell;
            float perCell = 1f / cellArea;   // one plant per cell at High (layer scale 16/m² = one per 0.25 m cell)
            var buffer = new List<int>(256);

            // ---- 2. ajisai (HY)
            int hy = (int)VegetationType.Hydrangea;
            VegetationRecipe.Species hySp = recipe.Get(VegetationType.Hydrangea);
            float hyRadiusMin = Mathf.Max(grid.Cell, ac.hydrangeaRadiusMin), hyRadiusMax = Mathf.Max(hyRadiusMin, ac.hydrangeaRadiusMax);

            // A cell takes a plant only where the placer keeps it (a cap under half the minimum height drops it).
            bool HydrangeaCell(int j) => ground[j] && field.Layers[hy][j] == 0 && HydrangeaTall(j) && HydrangeaMaxHeight(j) >= hySp.heightMin * 0.5f + 1e-3f;

            bool HydrangeaTall(int j)
            {
                if (Edge(grid, j, corridorSeed, half) > ac.flowerTallCorridorDistance) return true;
                return HydrangeaMaxHeight(j) <= ac.flowerTallCorridorHeight + 1e-4f;
            }

            float HydrangeaMaxHeight(int j)
            {
                float h = hySp.heightMax * field.HeightScaleAt(j);
                float cap = field.HeightCapAt(j);
                // w4: any zone cap under the ajisai cap is lifted for the ajisai cell (moss court 0.15, pond bank 0.45, courtyard 0.5)
                if (zoneOf[j] == (byte)VegetationZone.MossCourt || (cap > 0f && cap < ac.mossCourtHydrangeaCap)) cap = ac.mossCourtHydrangeaCap;
                // w5: in or near the corridor the ajisai is held to the corridor height instead of being dropped
                if (Edge(grid, j, corridorSeed, half) <= ac.flowerTallCorridorDistance) cap = cap > 0f ? Mathf.Min(cap, ac.flowerTallCorridorHeight) : ac.flowerTallCorridorHeight;
                return cap > 0f ? Mathf.Min(h, cap) : h;
            }

            bool HydrangeaSite(int j, bool anchor)
            {
                switch ((VegetationZone)zoneOf[j])
                {
                    case VegetationZone.PondBank:
                    case VegetationZone.WallBaseShadow:
                    case VegetationZone.GroveSkirt:
                        return true;
                    case VegetationZone.BuildingSurround:
                    case VegetationZone.BuildingShadow:
                        return true;   // w4 (user "sprinkled in between"): along the whole building band, not only its corners
                    case VegetationZone.Courtyard:
                        return !anchor || Corner(grid, j, dist.Building);
                    case VegetationZone.MossCourt:
                        return courtSeed != null && Edge(grid, j, courtSeed, half) <= ac.mossCourtRim;
                    default:
                        return false;
                }
            }

            var hyCandidates = new List<int>(512);
            for (int z = 0; z < grid.Height; z += stride)
            {
                for (int x = 0; x < grid.Width; x += stride)
                {
                    int i = grid.Index(x, z);
                    if (HydrangeaCell(i) && HydrangeaSite(i, true)) hyCandidates.Add(i);
                }
            }

            SortByHash(hyCandidates, SaltHydrangea);
            var clusters = new List<HydrangeaCluster>(64);
            int groups = 0;
            int plantsMin = Mathf.Max(1, ac.hydrangeaPlantsMin), plantsMax = Mathf.Max(plantsMin, ac.hydrangeaPlantsMax);
            int groupMin = Mathf.Max(1, ac.hydrangeaGroupMin), groupMax = Mathf.Max(groupMin, ac.hydrangeaGroupMax);
            float plantSpacing = hySp.footprint * 0.5f;
            for (int k = 0; k < hyCandidates.Count && clusters.Count < ac.hydrangeaClusters; k++)
            {
                int i = hyCandidates[k];
                Vector2 p = grid.CellCenter(i);
                float r = Mathf.Lerp(hyRadiusMin, hyRadiusMax, VegetationHash.Cell01(i, 1, SaltHydrangea));
                if (!HydrangeaFree(placed, p, r, -1, ac)) continue;
                int plants = plantsMin + (int)(VegetationHash.Cell01(i, 2, SaltHydrangea) * (plantsMax - plantsMin + 1));
                List<int> picked = PlantDisc(grid, p, r, HydrangeaCell, Mathf.Min(plants, plantsMax), plantsMin, plantSpacing, SaltHydrangea + 1, buffer);
                if (picked == null) continue;
                int group = groups++;
                AddHydrangea(clusters, placed, grid, i, p, r, group, picked, dist, zoneOf, shadowDir, groveTree, ac, tr, half);

                // Asymmetric group: satellites around the anchor at uneven gaps, turning 100–150° (a scalene triangle at 3).
                int size = Mathf.Min(groupMax, groupMin + (int)(VegetationHash.Cell01(i, 3, SaltHydrangea) * (groupMax - groupMin + 1)));
                float baseAngle = VegetationHash.Cell01(i, 4, SaltHydrangea) * 360f;
                float turn = Mathf.Lerp(100f, 150f, VegetationHash.Cell01(i, 5, SaltHydrangea)) * (VegetationHash.Cell01(i, 6, SaltHydrangea) < 0.5f ? 1f : -1f);
                for (int s = 1; s < size && clusters.Count < ac.hydrangeaClusters; s++)
                {
                    float rs = Mathf.Lerp(hyRadiusMin, hyRadiusMax, VegetationHash.Cell01(i, 10 + s, SaltHydrangea));
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        float angle = (baseAngle + s * turn + attempt * 47f) * Mathf.Deg2Rad;
                        float gap = Mathf.Lerp(ac.hydrangeaGroupGapMin, ac.hydrangeaGroupGapMax, VegetationHash.Cell01(i, 20 + s * 4 + attempt, SaltHydrangea));
                        Vector2 q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (r + rs + gap);
                        if (!grid.TryCell(q.x, q.y, out int qx, out int qz)) continue;
                        int qi = grid.Index(qx, qz);
                        if (!HydrangeaCell(qi) || !HydrangeaSite(qi, false) || !HydrangeaFree(placed, q, rs, group, ac)) continue;
                        int satPlants = plantsMin + (int)(VegetationHash.Cell01(qi, 2, SaltHydrangea) * (plantsMax - plantsMin + 1));
                        List<int> satPicked = PlantDisc(grid, q, rs, HydrangeaCell, Mathf.Min(satPlants, plantsMax), plantsMin, plantSpacing, SaltHydrangea + 1, buffer);
                        if (satPicked == null) continue;
                        AddHydrangea(clusters, placed, grid, qi, q, rs, group, satPicked, dist, zoneOf, shadowDir, groveTree, ac, tr, half);
                        break;
                    }
                }
            }

            // One colour per cluster: C by hash (anywhere), then A to the wettest / shadiest, B the rest (sun).
            AssignHydrangeaColours(clusters, ac, SaltHydrangea + 2);
            for (int c = 0; c < clusters.Count; c++)
            {
                HydrangeaCluster cl = clusters[c];
                for (int k = 0; k < cl.Plants.Count; k++)
                {
                    int j = cl.Plants[k];
                    field.SetDensity(hy, j, perCell);
                    field.VariantHint[j] = VegetationPlacer.WithAccentVariant(field.VariantHint[j], VegetationType.Hydrangea, cl.Colour);
                    // the cell's cap becomes the ajisai's own: lifted over a low zone cap (w4), held to the corridor height (w5)
                    float want = HydrangeaMaxHeight(j);
                    float cap = field.HeightCapAt(j);
                    if (cap > 0f ? Mathf.Abs(cap - want) > 0.02f : want < hySp.heightMax * field.HeightScaleAt(j) - 1e-3f) field.SetHeight(j, field.HeightScaleAt(j), want);
                }

                counts.HydrangeaPlants += cl.Plants.Count;
                if (cl.Colour == 0) counts.HydrangeaA++;
                else if (cl.Colour == 1) counts.HydrangeaB++;
                else counts.HydrangeaC++;
            }

            counts.HydrangeaGroups = groups;
            counts.HydrangeaClusters = clusters.Count;

            // ---- 3. hanashōbu (SL variants 1 / 2): shore strips 0.5–1.2 m from the water
            VegetationRecipe.Species slSp = recipe.Get(VegetationType.SpiderLily);
            bool IrisCell(int j)
            {
                if (!ground[j] || field.Layers[sl][j] != 0 || !Keeps(field, j, slSp)) return false;
                float dW = Edge(grid, j, dist.Water, half);
                return dW >= ac.irisWaterMin && dW <= ac.irisWaterMax;
            }

            var irisCandidates = new List<int>(256);
            for (int z = 0; z < grid.Height; z += stride)
            {
                for (int x = 0; x < grid.Width; x += stride)
                {
                    int i = grid.Index(x, z);
                    var zone = (VegetationZone)zoneOf[i];
                    if ((zone == VegetationZone.PondBank || zone == VegetationZone.PondShore) && IrisCell(i)) irisCandidates.Add(i);
                }
            }

            SortByHash(irisCandidates, SaltIris);
            int irisTarget = ac.irisStripsMin + (int)(VegetationHash.Cell01(7, 7, SaltIris) * (ac.irisStripsMax - ac.irisStripsMin + 1));
            int irisMin = Mathf.Max(1, ac.irisPerStripMin), irisMax = Mathf.Max(irisMin, ac.irisPerStripMax);
            for (int k = 0; k < irisCandidates.Count && counts.IrisStrips < irisTarget; k++)
            {
                int i = irisCandidates[k];
                Vector2 p = grid.CellCenter(i);
                int s = dist.Water[i];
                Vector2 normal = s >= 0 ? p - grid.CellCenter(s) : Vector2.right;
                if (normal.sqrMagnitude < 1e-6f) normal = Vector2.right;
                normal.Normalize();
                var along = new Vector2(-normal.y, normal.x);
                float length = Mathf.Lerp(ac.irisLengthMin, ac.irisLengthMax, VegetationHash.Cell01(i, 1, SaltIris));
                float width = Mathf.Max(grid.Cell, Mathf.Lerp(ac.irisWidthMin, ac.irisWidthMax, VegetationHash.Cell01(i, 2, SaltIris)));
                Vector2 a = p - along * (length * 0.5f), b = p + along * (length * 0.5f);
                if (!IrisFree(placed, a, b, width * 0.5f, ac)) continue;
                int plants = irisMin + (int)(VegetationHash.Cell01(i, 3, SaltIris) * (irisMax - irisMin + 1));
                List<int> picked = PlantStrip(grid, p, along, normal, length, width, IrisCell, Mathf.Min(plants, irisMax), irisMin, SaltIris + 1, buffer);
                if (picked == null) continue;
                int variant = VegetationHash.Cell01(i, 4, SaltIris) < 0.5f ? 1 : 2;
                WriteSpiderLily(field, picked, perCell, variant);
                placed.Add(new Capsule { A = a, B = b, R = width * 0.5f, Kind = KindIris, Group = -1 });
                counts.IrisStrips++;
                counts.IrisPlants += picked.Count;
            }

            // ---- 4. kurinsō (SL variant 3) + fuki (LC) leaves: shaded edges
            int lc = (int)VegetationType.LeafClump;
            bool PrimroseCell(int j) => ground[j] && field.Layers[sl][j] == 0 && Keeps(field, j, slSp);
            bool LeafCell(int j) => ground[j];

            bool PrimroseSite(int j)
            {
                Vector2 c = grid.CellCenter(j);
                switch ((VegetationZone)zoneOf[j])
                {
                    case VegetationZone.WallBaseShadow:
                    case VegetationZone.BuildingShadow:
                    case VegetationZone.BambooEdge:
                        return true;
                    case VegetationZone.PondBank:
                    {
                        // The sun-side bank: it slopes down to the water away from the sun, so its face is in shade.
                        int s = dist.Water[j];
                        if (s < 0) return false;
                        Vector2 v = c - grid.CellCenter(s);
                        return v.sqrMagnitude > 1e-6f && Vector2.Dot(v.normalized, shadowDir) < -tr.shadowFaceDot;
                    }
                    case VegetationZone.GroveSkirt:
                    {
                        if (groveTree[j] < 0) return false;
                        Vector2 v = c - grid.Trees[groveTree[j]].Position;
                        return v.sqrMagnitude > 1e-6f && Vector2.Dot(v.normalized, shadowDir) > tr.shadowFaceDot;
                    }
                    default:
                        return false;
                }
            }

            var primroseCandidates = new List<int>(256);
            for (int z = 0; z < grid.Height; z += stride)
            {
                for (int x = 0; x < grid.Width; x += stride)
                {
                    int i = grid.Index(x, z);
                    if (PrimroseCell(i) && PrimroseSite(i)) primroseCandidates.Add(i);
                }
            }

            SortByHash(primroseCandidates, SaltPrimrose);
            int primroseTarget = ac.primroseClustersMin + (int)(VegetationHash.Cell01(7, 7, SaltPrimrose) * (ac.primroseClustersMax - ac.primroseClustersMin + 1));
            int prMin = Mathf.Max(1, ac.primrosePlantsMin), prMax = Mathf.Max(prMin, ac.primrosePlantsMax);
            int leafMin = Mathf.Max(0, ac.primroseLeavesMin), leafMax = Mathf.Max(leafMin, ac.primroseLeavesMax);
            float prRadiusMin = Mathf.Max(grid.Cell, ac.primroseRadiusMin), prRadiusMax = Mathf.Max(prRadiusMin, ac.primroseRadiusMax);
            float lcCap = recipe.Get(VegetationType.LeafClump).maxDensity;
            var ring = new List<int>(64);
            for (int k = 0; k < primroseCandidates.Count && counts.PrimroseClusters < primroseTarget; k++)
            {
                int i = primroseCandidates[k];
                Vector2 p = grid.CellCenter(i);
                float r = Mathf.Lerp(prRadiusMin, prRadiusMax, VegetationHash.Cell01(i, 1, SaltPrimrose));
                float outer = r + Mathf.Max(0f, ac.primroseLeafRing);
                if (!PrimroseFree(placed, p, outer, ac)) continue;
                int plants = prMin + (int)(VegetationHash.Cell01(i, 2, SaltPrimrose) * (prMax - prMin + 1));
                List<int> picked = PlantDisc(grid, p, r, PrimroseCell, Mathf.Min(plants, prMax), prMin, 0f, SaltPrimrose + 1, buffer);
                if (picked == null) continue;
                WriteSpiderLily(field, picked, perCell, 3);

                // Fuki leaves in the ring around the flowers: the average density that lands 3–4 leaves (layer clamp aside).
                int leaves = leafMin + (int)(VegetationHash.Cell01(i, 3, SaltPrimrose) * (leafMax - leafMin + 1));
                ring.Clear();
                VegetationWorldProbe.ForCellsInRect(grid, p.x - outer, p.y - outer, p.x + outer, p.y + outer, j =>
                {
                    float d2 = (grid.CellCenter(j) - p).sqrMagnitude;
                    if (d2 > r * r && d2 <= outer * outer && LeafCell(j)) ring.Add(j);
                });
                if (leaves > 0 && ring.Count > 0)
                {
                    float leafDensity = Mathf.Min(lcCap, leaves / (ring.Count * cellArea));
                    for (int q = 0; q < ring.Count; q++)
                    {
                        int j = ring[q];
                        if (leafDensity > field.Density(lc, j)) field.SetDensity(lc, j, leafDensity);
                    }
                }

                placed.Add(new Capsule { A = p, B = p, R = outer, Kind = KindPrimrose, Group = -1 });
                counts.PrimroseClusters++;
                counts.PrimrosePlants += picked.Count;
            }

            LastAccents = counts;
        }

        /// <summary>
        /// AD v1.2 (10.10) flower ground: growable, not corridor, outside the clear radii, not on karesansui / desire path /
        /// bare soil / bridge approach / plaza edge / plaza joint / path edge / under a crown (0–0.8 R, and a forced grove),
        /// ≥ 0.8 m from a path, ≥ 1.5 m from the plaza, ≥ 3 m from a spawn point, ≥ 2.5 m from a weapon / door point.
        /// </summary>
        private static bool[] FlowerGround(VegetationGrid grid, VegetationRecipe recipe, Distances dist, byte[] zoneOf, bool[] corridorOf, byte[] clear, float[] groveR)
        {
            VegetationRecipe.Accents ac = recipe.accents;
            VegetationRecipe.Transitions tr = recipe.transitions;
            float half = grid.Cell * 0.5f;
            var ok = new bool[grid.Count];
            // w5 (10.10): the corridor's interior stays clear, its edge band takes flowers (V6's corridor mask covers 100 % of the pond
            // bank, 88 % of the building band and 80 % of the lawn — excluding all of it left w4 with 6 ajisai clusters, 0 hanashōbu,
            // 0 kurinsō). Edge = distance to the nearest cell outside the corridor (water, wall, building, path, open ground).
            var outsideCorridor = new bool[grid.Count];
            for (int i = 0; i < ok.Length; i++) outsideCorridor[i] = !corridorOf[i];
            var outsideSeed = new int[grid.Count];
            NearestSeeds(outsideCorridor, grid.Width, grid.Height, outsideSeed);
            for (int i = 0; i < ok.Length; i++)
            {
                if (!grid.Growable(i) || zoneOf[i] == 0 || clear[i] != 0) continue;
                if (corridorOf[i] && Edge(grid, i, outsideSeed, half) > ac.flowerCorridorMargin) continue;
                if (!FlowerZone((VegetationZone)zoneOf[i]) || groveR[i] < tr.groveInner) continue;
                if (Edge(grid, i, dist.Path, half) < ac.flowerPathClear || Edge(grid, i, dist.Plaza, half) < ac.flowerPlazaClear) continue;
                // The rects / circles too: the zone priority puts shore and pond bank before the bridge approach.
                Vector2 c = grid.CellCenter(i);
                if (InRects(recipe.regions.karesansui, c) || InApproach(recipe.regions.bridgeApproaches, c)) continue;
                ok[i] = true;
            }

            for (int k = 0; k < grid.ClearPoints.Count; k++)
            {
                VegetationGrid.ClearPoint cp = grid.ClearPoints[k];
                float r = cp.Kind == VegetationGrid.ClearKind.Spawn
                    ? Mathf.Max(cp.Radius, ac.flowerSpawnClear)
                    : cp.Radius + Mathf.Max(0f, ac.flowerPickupClear - (cp.Kind == VegetationGrid.ClearKind.Weapon ? tr.weaponClear : tr.doorClear));
                VegetationWorldProbe.ForCellsInRect(grid, cp.Position.x - r, cp.Position.y - r, cp.Position.x + r, cp.Position.y + r, j =>
                {
                    if ((grid.CellCenter(j) - cp.Position).sqrMagnitude <= r * r) ok[j] = false;
                });
            }

            return ok;
        }

        private static bool FlowerZone(VegetationZone zone)
        {
            switch (zone)
            {
                case VegetationZone.Auto:
                case VegetationZone.Karesansui:
                case VegetationZone.KaresansuiCollar:
                case VegetationZone.DesirePath:
                case VegetationZone.BareSoil:
                case VegetationZone.BridgeApproach:
                case VegetationZone.PlazaEdge:
                case VegetationZone.PlazaJoint:
                case VegetationZone.PathEdge:
                case VegetationZone.Grove:
                    return false;
                default:
                    return true;
            }
        }

        private static void AddHydrangea(List<HydrangeaCluster> clusters, List<Capsule> placed, VegetationGrid grid, int cell, Vector2 p, float r, int group,
            List<int> plants, Distances dist, byte[] zoneOf, Vector2 shadowDir, int[] groveTree, VegetationRecipe.Accents ac, VegetationRecipe.Transitions tr, float half)
        {
            var zone = (VegetationZone)zoneOf[cell];
            bool shade = Edge(grid, cell, dist.Water, half) < ac.hydrangeaShadeReach
                         || zone == VegetationZone.WallBaseShadow || zone == VegetationZone.BuildingShadow
                         || (Edge(grid, cell, dist.Building, half) < ac.hydrangeaShadeReach && ShadowFace(grid, cell, dist.Building, shadowDir, tr.shadowFaceDot))
                         || (Edge(grid, cell, dist.Wall, half) < ac.hydrangeaShadeReach && ShadowFace(grid, cell, dist.Wall, shadowDir, tr.shadowFaceDot));
            if (!shade && zone == VegetationZone.GroveSkirt && groveTree[cell] >= 0)
            {
                Vector2 v = p - grid.Trees[groveTree[cell]].Position;
                shade = v.sqrMagnitude > 1e-6f && Vector2.Dot(v.normalized, shadowDir) > tr.shadowFaceDot;
            }

            clusters.Add(new HydrangeaCluster { Centre = p, Radius = r, Cell = cell, Group = group, Shade = shade, Plants = plants });
            placed.Add(new Capsule { A = p, B = p, R = r, Kind = KindHydrangea, Group = group });
        }

        /// <summary>Shares (A, B, C): C first by hash; of the rest, A goes to the shaded clusters first (hash among equals), then B.</summary>
        private static void AssignHydrangeaColours(List<HydrangeaCluster> clusters, VegetationRecipe.Accents ac, int salt)
        {
            int n = clusters.Count;
            if (n == 0) return;
            float[] share = ac.hydrangeaShares;
            float sa = share != null && share.Length > 0 ? Mathf.Max(0f, share[0]) : 0.40f;
            float sb = share != null && share.Length > 1 ? Mathf.Max(0f, share[1]) : 0.35f;
            float sc = share != null && share.Length > 2 ? Mathf.Max(0f, share[2]) : 0.25f;
            float sum = Mathf.Max(1e-4f, sa + sb + sc);
            int nC = Mathf.Clamp(Mathf.RoundToInt(n * sc / sum), 0, n);
            int nA = Mathf.Clamp(Mathf.RoundToInt(n * sa / sum), 0, n - nC);

            var order = new List<int>(n);
            for (int k = 0; k < n; k++) order.Add(k);
            order.Sort((x, y) => VegetationHash.Cell(clusters[x].Cell, 0, salt).CompareTo(VegetationHash.Cell(clusters[y].Cell, 0, salt)));
            for (int k = 0; k < n; k++) clusters[order[k]].Colour = k < nC ? 2 : -1;

            var rest = new List<int>(n);
            for (int k = nC; k < n; k++) rest.Add(order[k]);
            rest.Sort((x, y) =>
            {
                int s = (clusters[y].Shade ? 1 : 0).CompareTo(clusters[x].Shade ? 1 : 0);
                return s != 0 ? s : VegetationHash.Cell(clusters[x].Cell, 1, salt).CompareTo(VegetationHash.Cell(clusters[y].Cell, 1, salt));
            });
            for (int k = 0; k < rest.Count; k++) clusters[rest[k]].Colour = k < nA ? 0 : 1;
        }

        /// <summary>The placer keeps a plant of <paramref name="sp"/> in cell <paramref name="j"/>: no cap, or a cap ≥ half its minimum height.</summary>
        private static bool Keeps(VegetationDensityField field, int j, VegetationRecipe.Species sp)
        {
            float cap = field.HeightCapAt(j);
            return cap <= 0f || cap >= sp.heightMin * 0.5f + 1e-3f;
        }

        private static void WriteSpiderLily(VegetationDensityField field, List<int> cells, float perCell, int variant)
        {
            int sl = (int)VegetationType.SpiderLily;
            for (int k = 0; k < cells.Count; k++)
            {
                int j = cells[k];
                field.SetDensity(sl, j, perCell);
                field.VariantHint[j] = VegetationPlacer.WithAccentVariant(field.VariantHint[j], VegetationType.SpiderLily, variant);
            }
        }

        // ---- gap rules (edge to edge, capsules)

        private static bool HydrangeaFree(List<Capsule> placed, Vector2 p, float r, int group, VegetationRecipe.Accents ac)
        {
            for (int k = 0; k < placed.Count; k++)
            {
                Capsule c = placed[k];
                float gap = CapsuleGap(c, p, p, r);
                if (c.Kind != KindHydrangea)
                {
                    if (gap < ac.flowerAccentGap) return false;
                }
                else if (group < 0)
                {
                    if ((c.A - p).sqrMagnitude < ac.hydrangeaSpacing * ac.hydrangeaSpacing) return false;   // a new group's anchor
                }
                else if (c.Group == group)
                {
                    if (gap < ac.hydrangeaGroupGapMin - 1e-3f) return false;
                }
                else if (gap < ac.hydrangeaGroupGapMin || (c.A - p).sqrMagnitude < 0.25f * ac.hydrangeaSpacing * ac.hydrangeaSpacing)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IrisFree(List<Capsule> placed, Vector2 a, Vector2 b, float r, VegetationRecipe.Accents ac)
        {
            for (int k = 0; k < placed.Count; k++)
            {
                Capsule c = placed[k];
                float need = c.Kind == KindHiganbana ? ac.irisHiganbanaGap : c.Kind == KindIris ? ac.irisGap : ac.flowerAccentGap;
                if (CapsuleGap(c, a, b, r) < need) return false;
            }

            return true;
        }

        private static bool PrimroseFree(List<Capsule> placed, Vector2 p, float r, VegetationRecipe.Accents ac)
        {
            for (int k = 0; k < placed.Count; k++)
            {
                Capsule c = placed[k];
                if (c.Kind == KindPrimrose)
                {
                    if ((c.A - p).sqrMagnitude < ac.primroseSpacing * ac.primroseSpacing) return false;
                }
                else if (CapsuleGap(c, p, p, r) < ac.flowerAccentGap)
                {
                    return false;
                }
            }

            return true;
        }

        private static float CapsuleGap(Capsule c, Vector2 a, Vector2 b, float r)
        {
            return SegmentDistance(c.A, c.B, a, b) - c.R - r;
        }

        private static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return (a + ab * t - p).magnitude;
        }

        private static float SegmentDistance(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            if (SegmentsCross(a0, a1, b0, b1)) return 0f;
            return Mathf.Min(Mathf.Min(PointSegment(a0, b0, b1), PointSegment(a1, b0, b1)), Mathf.Min(PointSegment(b0, a0, a1), PointSegment(b1, a0, a1)));
        }

        private static bool SegmentsCross(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            float d1 = Cross(b1 - b0, a0 - b0), d2 = Cross(b1 - b0, a1 - b0);
            float d3 = Cross(a1 - a0, b0 - a0), d4 = Cross(a1 - a0, b1 - a0);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        // ---- plant cells (one plant per cell; hash order = an uneven, deterministic spread)

        /// <summary>
        /// Up to <paramref name="plants"/> cells of the disc that pass <paramref name="ok"/>, hash-ordered, each ≥
        /// <paramref name="spacing"/> from the others; null when less than <see cref="GroundShare"/> of the disc is flower
        /// ground or fewer than <paramref name="minimum"/> fit.
        /// </summary>
        private static List<int> PlantDisc(VegetationGrid grid, Vector2 p, float r, System.Func<int, bool> ok, int plants, int minimum, float spacing, int salt, List<int> buffer)
        {
            buffer.Clear();
            int total = 0;
            VegetationWorldProbe.ForCellsInRect(grid, p.x - r, p.y - r, p.x + r, p.y + r, j =>
            {
                if ((grid.CellCenter(j) - p).sqrMagnitude > r * r) return;
                total++;
                if (ok(j)) buffer.Add(j);
            });
            return Pick(grid, buffer, total, plants, minimum, spacing, salt);
        }

        private static List<int> PlantStrip(VegetationGrid grid, Vector2 p, Vector2 along, Vector2 normal, float length, float width, System.Func<int, bool> ok, int plants, int minimum, int salt, List<int> buffer)
        {
            buffer.Clear();
            int total = 0;
            float reach = length * 0.5f + width;
            VegetationWorldProbe.ForCellsInRect(grid, p.x - reach, p.y - reach, p.x + reach, p.y + reach, j =>
            {
                Vector2 v = grid.CellCenter(j) - p;
                if (Mathf.Abs(Vector2.Dot(v, along)) > length * 0.5f || Mathf.Abs(Vector2.Dot(v, normal)) > width * 0.5f) return;
                total++;
                if (ok(j)) buffer.Add(j);
            });
            return Pick(grid, buffer, total, plants, minimum, 0f, salt);
        }

        private static List<int> Pick(VegetationGrid grid, List<int> cells, int total, int plants, int minimum, float spacing, int salt)
        {
            if (total == 0 || cells.Count < GroundShare * total || cells.Count < minimum) return null;
            SortByHash(cells, salt);
            var picked = new List<int>(plants);
            for (int k = 0; k < cells.Count && picked.Count < plants; k++)
            {
                Vector2 c = grid.CellCenter(cells[k]);
                bool free = true;
                for (int q = 0; q < picked.Count && free; q++) free = (grid.CellCenter(picked[q]) - c).sqrMagnitude >= spacing * spacing;
                if (free) picked.Add(cells[k]);
            }

            return picked.Count >= minimum ? picked : null;
        }

        private static bool Corner(VegetationGrid grid, int i, int[] seed)
        {
            int s = seed[i];
            if (s < 0) return false;
            Vector2 v = grid.CellCenter(i) - grid.CellCenter(s);
            float m = v.magnitude;
            return m > 0.3f && m < 2.0f && Mathf.Abs(v.x) > 0.35f * m && Mathf.Abs(v.y) > 0.35f * m;
        }

        private static bool TooClose(List<Vector2> picked, Vector2 p, float spacing)
        {
            for (int k = 0; k < picked.Count; k++)
            {
                if ((picked[k] - p).sqrMagnitude < spacing * spacing) return true;
            }

            return false;
        }

        private static void SortByHash(List<int> cells, int salt)
        {
            cells.Sort((a, b) => VegetationHash.Cell(a, 0, salt).CompareTo(VegetationHash.Cell(b, 0, salt)));
        }
    }
}
