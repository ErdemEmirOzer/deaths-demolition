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
    /// Accents (hydrangea clusters, spider-lily strips) are a deterministic post-pass. Load time only.
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

            Accents(grid, recipe, field, dist, zoneOf, corridorOf, clear, shadowDir);
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

        // ------------------------------------------------------------------ accents

        private static void Accents(VegetationGrid grid, VegetationRecipe recipe, VegetationDensityField field, Distances dist, byte[] zoneOf, bool[] corridorOf, byte[] clear, Vector2 shadowDir)
        {
            VegetationRecipe.Accents ac = recipe.accents;
            VegetationRecipe.Transitions tr = recipe.transitions;
            float half = grid.Cell * 0.5f;
            int stride = Mathf.Max(1, Mathf.RoundToInt(1f / grid.Cell));

            // ---- hydrangea clusters: pond bank off-corridor, building corners, wall bases beside doors
            var candidates = new List<int>(512);
            for (int z = 0; z < grid.Height; z += stride)
            {
                for (int x = 0; x < grid.Width; x += stride)
                {
                    int i = grid.Index(x, z);
                    if (!grid.Growable(i) || corridorOf[i] || zoneOf[i] == 0 || clear[i] != 0) continue;
                    var zone = (VegetationZone)zoneOf[i];
                    bool ok = false;
                    if (zone == VegetationZone.PondBank) ok = true;
                    else if (zone == VegetationZone.BuildingSurround || zone == VegetationZone.BuildingShadow) ok = Corner(grid, i, dist.Building);
                    else if (zone == VegetationZone.WallBase || zone == VegetationZone.WallBaseShadow) ok = NearDoor(grid, i, 1.5f, 6f);
                    if (ok && FarFromClear(grid, i, ac.hydrangeaClearance)) candidates.Add(i);
                }
            }

            SortByHash(candidates, SaltAccent);
            var picked = new List<Vector2>(64);
            int hy = (int)VegetationType.Hydrangea;
            for (int k = 0; k < candidates.Count && picked.Count < ac.hydrangeaClusters; k++)
            {
                int i = candidates[k];
                Vector2 p = grid.CellCenter(i);
                if (TooClose(picked, p, ac.hydrangeaSpacing)) continue;
                picked.Add(p);
                int plants = ac.hydrangeaPlantsMin + (int)(VegetationHash.Cell01(i, 0, SaltAccent + 1) * (ac.hydrangeaPlantsMax - ac.hydrangeaPlantsMin + 1));
                float r = Mathf.Max(grid.Cell, ac.hydrangeaRadius);
                float density = plants / (Mathf.PI * r * r);
                bool blue = Edge(grid, i, dist.Water, half) < 4f || ShadowFace(grid, i, dist.Building, shadowDir, tr.shadowFaceDot) || ShadowFace(grid, i, dist.Wall, shadowDir, tr.shadowFaceDot);
                VegetationWorldProbe.ForCellsInRect(grid, p.x - r, p.y - r, p.x + r, p.y + r, j =>
                {
                    if (!grid.Growable(j) || (grid.CellCenter(j) - p).sqrMagnitude > r * r) return;
                    field.SetDensity(hy, j, density);
                    field.VariantHint[j] = blue ? (byte)1 : (byte)2;
                });
            }

            // ---- spider-lily strips: inside the bible's regions, along path edges or the pond bank
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
                float density = count / (length * width);
                float reach = length * 0.5f + width;
                VegetationWorldProbe.ForCellsInRect(grid, p.x - reach, p.y - reach, p.x + reach, p.y + reach, j =>
                {
                    if (!grid.Growable(j)) return;
                    Vector2 v = grid.CellCenter(j) - p;
                    if (Mathf.Abs(Vector2.Dot(v, along)) > length * 0.5f || Mathf.Abs(Vector2.Dot(v, normal)) > width * 0.5f) return;
                    field.SetDensity(sl, j, density);
                });
            }
        }

        private static bool Corner(VegetationGrid grid, int i, int[] seed)
        {
            int s = seed[i];
            if (s < 0) return false;
            Vector2 v = grid.CellCenter(i) - grid.CellCenter(s);
            float m = v.magnitude;
            return m > 0.3f && m < 2.0f && Mathf.Abs(v.x) > 0.35f * m && Mathf.Abs(v.y) > 0.35f * m;
        }

        private static bool NearDoor(VegetationGrid grid, int i, float min, float max)
        {
            Vector2 p = grid.CellCenter(i);
            for (int k = 0; k < grid.ClearPoints.Count; k++)
            {
                VegetationGrid.ClearPoint cp = grid.ClearPoints[k];
                if (cp.Kind != VegetationGrid.ClearKind.Door) continue;
                float d = (cp.Position - p).magnitude;
                if (d >= min + cp.Radius - 1.5f && d <= max) return true;
            }

            return false;
        }

        private static bool FarFromClear(VegetationGrid grid, int i, float clearance)
        {
            Vector2 p = grid.CellCenter(i);
            for (int k = 0; k < grid.ClearPoints.Count; k++)
            {
                VegetationGrid.ClearPoint cp = grid.ClearPoints[k];
                if ((cp.Position - p).magnitude < cp.Radius + clearance) return false;
            }

            return true;
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
