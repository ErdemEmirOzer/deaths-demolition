using System.Diagnostics;
using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// WP-030 (ADR-030-1) · Regional routing on top of the NavMesh: route comparison through
    /// the profile's volumes (cost multipliers, optional alternative waypoint) and chokepoint
    /// occupancy. Stateless apart from counters; allocation-free on the per-tick paths
    /// (fixed corner buffer, no LINQ, no closures). Used by <see cref="NavPathFollower"/>.
    ///
    /// Route comparison: the effective cost of a corner list = Σ segment length × the cost
    /// multiplier of the volume the segment's midpoint lies in (1 outside volumes). When the
    /// direct route crosses a costly volume that offers an alternative, the route through the
    /// alternative waypoint (two NavMesh queries, budgeted) is taken when
    /// altCost &lt; directCost × (1 + routeRoll × RouteBiasSpread) - so members with a high
    /// roll spread over both routes (REQ-112) while nobody takes a route that is plainly worse.
    /// </summary>
    public static class RegionNavRouter
    {
        private static readonly Vector3[] s_altA = new Vector3[NavPathService.MaxCorners];
        private static readonly Vector3[] s_altB = new Vector3[NavPathService.MaxCorners];
        private static readonly Stopwatch s_watch = new Stopwatch();

        public static int RerouteCount { get; private set; }
        /// <summary>WP-055B: bodies that switched from the direct route to the alternative (a per-body edge, not the per-refresh decision count above — WP-038 read 163 decisions as 163 reroutes).</summary>
        public static int RouteSwitchCount { get; private set; }
        public static void NoteRouteSwitch() => RouteSwitchCount++;

        /// <summary>The alternative-route query shape (see <see cref="TryAlternative"/>).</summary>
        public delegate int AlternativeQuery(RegionNavProfile profile, NavRegionVolume volume, Vector3 position, Vector3 goal, Vector3[] buffer, out float altCost);

        /// <summary>
        /// WP-040 (WP-039 sanity E2): the alternative query <see cref="ChooseRoute"/> uses. Null = the live
        /// NavMesh query (<see cref="TryAlternative"/>), which legitimately finds an alternative whenever a
        /// baked NavMesh is loaded — in the Player, in PlayMode, AND in EditMode with a baked world scene open
        /// (WP-020). Tests that must not depend on what scene the editor holds install a deterministic query
        /// here (no NavMesh → 0; a synthetic alternative → its corners); <see cref="ResetStatics"/> clears it.
        /// </summary>
        public static AlternativeQuery AlternativeQueryOverride { get; set; }
        public static int QueueEvents { get; private set; }
        public static int AreaCostsApplied { get; private set; }
        public static bool AreaCostsAppliedOnce { get; private set; }
        public static double LastCompareMilliseconds { get; private set; }

        public static void ResetStatics()
        {
            AlternativeQueryOverride = null;
            RegionNavProfile.ResetBridgeRuntimeState();
            RerouteCount = 0;
            RouteSwitchCount = 0;
            QueueEvents = 0;
            AreaCostsApplied = 0;
            AreaCostsAppliedOnce = false;
            LastCompareMilliseconds = 0;
        }

        /// <summary>Post-bake assignment (once per scene load): NavMesh area costs for named areas, when the bake has them.</summary>
        public static void EnsureAreaCosts()
        {
            if (AreaCostsAppliedOnce) return;
            AreaCostsAppliedOnce = true;
            AreaCostsApplied = RegionNavProfile.Active.ApplyAreaCosts();
        }

        /// <summary>Σ segment length × volume multiplier over <paramref name="count"/> corners; also reports the first costly volume with an alternative on the route (null when none).</summary>
        public static float EffectiveCost(RegionNavProfile profile, Vector3[] corners, int count, out NavRegionVolume firstCostly)
        {
            firstCostly = null;
            float cost = 0f;
            for (int i = 1; i < count; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i];
                float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                NavRegionVolume v = profile.Find((a + b) * 0.5f);
                if (v == null) v = profile.Find(b);
                float mul = v != null ? v.costMultiplier : 1f;
                cost += len * mul;
                if (firstCostly == null && v != null && v.IsCostly && v.hasAlternative) firstCostly = v;
            }

            return cost;
        }

        /// <summary>
        /// Try the route position → volume.alternative → goal. On success the corners are written
        /// into <paramref name="buffer"/> and the effective cost returned; otherwise -1. Costs two
        /// path queries from the frame budget (returns -1 when the budget is spent).
        /// </summary>
        public static int TryAlternative(RegionNavProfile profile, NavRegionVolume volume, Vector3 position, Vector3 goal, Vector3[] buffer, out float altCost)
        {
            altCost = -1f;
            if (volume == null || !volume.hasAlternative || buffer == null) return 0;
            NavPathService paths = CrowdService.Paths;
            if (paths == null || !paths.TryProject(volume.alternative, 6f, out Vector3 via)) return 0;
            if (!paths.TryTakeQueryBudget()) return 0;
            int na = paths.CalculateCorners(position, via, s_altA, out NavPathStatus sa);
            if (na < 2 || sa != NavPathStatus.Complete) return 0;
            if (!paths.TryTakeQueryBudget()) return 0;
            int nb = paths.CalculateCorners(via, goal, s_altB, out NavPathStatus sb);
            if (nb < 2 || sb != NavPathStatus.Complete) return 0;
            int n = 0;
            for (int i = 0; i < na && n < buffer.Length; i++) buffer[n++] = s_altA[i];
            for (int i = 1; i < nb && n < buffer.Length; i++) buffer[n++] = s_altB[i];   // skip the duplicated 'via'
            altCost = EffectiveCost(profile, buffer, n, out _);
            return n;
        }

        /// <summary>
        /// WP-088 (R2, V6 C8 / Waterfall): whether a detour through <paramref name="volume"/>'s alternative can still help — not when the
        /// deck is the destination or the start (going round it ends on it anyway), nor once the waypoint is no longer ahead (the body is
        /// as close to the goal as the waypoint, within <paramref name="margin"/> — RegionNavProfile.AlternativeAheadMargin). Without this a
        /// body beside the waypoint was sent back to it on every repath (the detour costs only ~2 × its distance, inside the acceptance band)
        /// and circled it.
        /// </summary>
        public static bool AlternativeAhead(NavRegionVolume volume, Vector3 position, Vector3 goal, float margin)
        {
            if (volume.Contains(goal) || volume.Contains(position)) return false;
            float body = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(goal.x, goal.z));
            float via = Vector2.Distance(new Vector2(volume.alternative.x, volume.alternative.z), new Vector2(goal.x, goal.z));
            return body > via + margin;
        }

        /// <summary>Decide between the direct corners (already in <paramref name="buffer"/>) and the alternative; rewrites the buffer when the alternative wins. Returns the corner count in use.</summary>
        public static int ChooseRoute(RegionNavProfile profile, Vector3 position, Vector3 goal, Vector3[] buffer, int directCount, float routeRoll, out bool rerouted)
        {
            rerouted = false;
            if (profile == null || directCount < 2) return directCount;
            s_watch.Restart();
            float direct = EffectiveCost(profile, buffer, directCount, out NavRegionVolume costly);
            if (costly == null || !AlternativeAhead(costly, position, goal, profile.AlternativeAheadMargin))
            {
                s_watch.Stop(); LastCompareMilliseconds = s_watch.Elapsed.TotalMilliseconds;
                return directCount;
            }

            Vector3[] alt = s_altScratch;
            AlternativeQuery query = AlternativeQueryOverride ?? TryAlternative;
            int altCount = query(profile, costly, position, goal, alt, out float altCost);
            s_watch.Stop(); LastCompareMilliseconds = s_watch.Elapsed.TotalMilliseconds;
            if (altCount < 2 || altCount > buffer.Length) return directCount;
            float acceptance = direct * (1f + Mathf.Clamp01(routeRoll) * profile.RouteBiasSpread);
            if (altCost < acceptance)
            {
                for (int i = 0; i < altCount; i++) buffer[i] = alt[i];
                rerouted = true;
                RerouteCount++;
                return altCount;
            }

            return directCount;
        }

        private static readonly Vector3[] s_altScratch = new Vector3[NavPathService.MaxCorners];

        /// <summary>Members inside the volume right now (crowd records; allocation-free).</summary>
        public static int Occupancy(NavRegionVolume volume)
        {
            CrowdSolver solver = CrowdService.Solver;
            return solver != null && volume != null ? solver.CountMembersInBox(volume.center, volume.size) : 0;
        }

        /// <summary>
        /// Chokepoint gate for a member outside <paramref name="next"/>'s volume whose next corner is inside it:
        /// true = queue (occupancy ≥ capacity and the member has not waited past MaxQueueSeconds).
        /// </summary>
        public static bool ShouldQueue(RegionNavProfile profile, Vector3 position, Vector3 nextCorner, float queuedSeconds, out NavRegionVolume volume)
        {
            volume = null;
            if (profile == null) return false;
            NavRegionVolume v = profile.Find(nextCorner);
            if (v == null || !v.IsChokepoint || v.Contains(position)) return false;
            volume = v;
            if (queuedSeconds >= profile.MaxQueueSeconds) return false;
            bool full = Occupancy(v) >= v.capacity;
            if (full && queuedSeconds <= 0f) QueueEvents++;
            return full;
        }

        /// <summary>Identity of one provisional Bridge entry; never a generic chokepoint reservation.</summary>
        public readonly struct BridgeAdmission
        {
            public readonly NavRegionVolume Volume;
            public readonly int Id;
            public readonly float Created;
            internal BridgeAdmission(NavRegionVolume volume, int id, float created)
            { Volume = volume; Id = id; Created = created; }
        }

        public static bool AtBridgeMouth(NavRegionVolume volume, Vector3 position, Vector3 nextCorner, float probe)
        {
            if (volume == null || volume.kind != NavRegionKind.Bridge || !volume.IsChokepoint || volume.Contains(position) || probe <= 0f) return false;
            Vector3 d = nextCorner - position;
            d.y = 0f;
            float length = d.magnitude;
            if (length < 0.0001f) return false;
            Vector3 end = position + d * (Mathf.Min(probe, length) / length);
            return BridgeSegmentIntersects(volume, position, end);
        }

        // Analytic XZ box clipping; Bridge scene volumes are rectangles. A polygon still uses its actual outline.
        private static bool BridgeSegmentIntersects(NavRegionVolume volume, Vector3 a, Vector3 b)
        {
            if (volume.Contains(a) || volume.Contains(b)) return true;
            if (volume.polygonXZ != null && volume.polygonXZ.Length >= 3)
            {
                // Segment/polygon edge intersection, including a narrow deck between two outside corners.
                Vector2 p = new Vector2(a.x, a.z), r = new Vector2(b.x - a.x, b.z - a.z);
                Vector2[] poly = volume.polygonXZ;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    Vector2 q = poly[j], e = poly[i] - q;
                    float cross = r.x * e.y - r.y * e.x;
                    if (Mathf.Abs(cross) < 0.000001f) continue;
                    Vector2 delta = q - p;
                    float t = (delta.x * e.y - delta.y * e.x) / cross;
                    float u = (delta.x * r.y - delta.y * r.x) / cross;
                    if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return true;
                }
                return false;
            }
            float lo = 0f, hi = 1f;
            return ClipBridgeAxis(a.x, b.x - a.x, volume.center.x - volume.size.x * 0.5f, volume.center.x + volume.size.x * 0.5f, ref lo, ref hi)
                && ClipBridgeAxis(a.z, b.z - a.z, volume.center.z - volume.size.z * 0.5f, volume.center.z + volume.size.z * 0.5f, ref lo, ref hi);
        }

        private static bool ClipBridgeAxis(float at, float delta, float min, float max, ref float lo, ref float hi)
        {
            if (Mathf.Abs(delta) < 0.000001f) return at >= min && at <= max;
            float a = (min - at) / delta, b = (max - at) / delta;
            if (a > b) { float swap = a; a = b; b = swap; }
            lo = Mathf.Max(lo, a); hi = Mathf.Min(hi, b);
            return lo <= hi;
        }

        private static bool BridgePathIntersects(NavRegionVolume bridge, Vector3[] corners, int count)
        {
            for (int i = 1; i < count; i++) if (BridgeSegmentIntersects(bridge, corners[i - 1], corners[i])) return true;
            return false;
        }

        private static bool PrepareBridgeClock(NavRegionVolume volume, float now)
        {
            if (volume == null || volume.kind != NavRegionKind.Bridge || volume.BridgeAdmissionIds == null
                || volume.BridgeAdmissionTimes == null || float.IsNaN(now) || float.IsInfinity(now)) return false;
            if (now < volume.BridgeLastClock) volume.ClearBridgeState();
            volume.BridgeLastClock = now;
            return true;
        }

        public static int RecentBridgeAdmissions(RegionNavProfile profile, NavRegionVolume bridge, float now, BridgeAdmission own = default)
        {
            if (profile == null || !PrepareBridgeClock(bridge, now)) return 0;
            int n = 0;
            float hold = Mathf.Max(0.1f, profile.BridgeAdmitHold);
            for (int i = 0; i < bridge.BridgeAdmissionIds.Length; i++)
            {
                int id = bridge.BridgeAdmissionIds[i];
                if (id == 0) continue;
                float created = bridge.BridgeAdmissionTimes[i];
                if (created > now || now - created >= hold) { bridge.BridgeAdmissionIds[i] = 0; continue; }
                if (ReferenceEquals(own.Volume, bridge) && own.Id == id && own.Created == created) continue;
                n++;
            }
            return n;
        }

        public static bool HasBridgeAdmission(RegionNavProfile profile, NavRegionVolume bridge, float now, BridgeAdmission own)
        {
            if (profile == null || !ReferenceEquals(own.Volume, bridge) || own.Id == 0 || !PrepareBridgeClock(bridge, now)) return false;
            if (own.Created > now || now - own.Created >= Mathf.Max(0.1f, profile.BridgeAdmitHold)) return false;
            for (int i = 0; i < bridge.BridgeAdmissionIds.Length; i++)
                if (bridge.BridgeAdmissionIds[i] == own.Id && bridge.BridgeAdmissionTimes[i] == own.Created) return true;
            return false;
        }

        public static int BridgeLoad(RegionNavProfile profile, NavRegionVolume bridge, float now, BridgeAdmission own = default)
        {
            if (bridge == null || bridge.kind != NavRegionKind.Bridge) return 0;
            return Occupancy(bridge) + RecentBridgeAdmissions(profile, bridge, now, own);
        }

        public static bool NoteBridgeAdmission(RegionNavProfile profile, NavRegionVolume bridge, float now, out BridgeAdmission admission)
        {
            admission = default;
            if (profile == null || bridge == null || !bridge.IsChokepoint || !PrepareBridgeClock(bridge, now)) return false;
            RecentBridgeAdmissions(profile, bridge, now); // Expire slots before reuse; no hot-path allocation.
            for (int offset = 0; offset < bridge.BridgeAdmissionIds.Length; offset++)
            {
                int i = (bridge.BridgeAdmissionCursor + offset) % bridge.BridgeAdmissionIds.Length;
                if (bridge.BridgeAdmissionIds[i] != 0) continue;
                int id = unchecked(++bridge.BridgeNextAdmissionId);
                if (id == 0) id = unchecked(++bridge.BridgeNextAdmissionId);
                bridge.BridgeAdmissionIds[i] = id;
                bridge.BridgeAdmissionTimes[i] = now;
                bridge.BridgeAdmissionCursor = (i + 1) % bridge.BridgeAdmissionIds.Length;
                admission = new BridgeAdmission(bridge, id, now);
                return true;
            }
            return false;
        }

        public static void CancelBridgeAdmission(BridgeAdmission admission)
        {
            NavRegionVolume bridge = admission.Volume;
            if (bridge == null || bridge.kind != NavRegionKind.Bridge || admission.Id == 0 || bridge.BridgeAdmissionIds == null) return;
            for (int i = 0; i < bridge.BridgeAdmissionIds.Length; i++)
                if (bridge.BridgeAdmissionIds[i] == admission.Id && bridge.BridgeAdmissionTimes[i] == admission.Created)
                { bridge.BridgeAdmissionIds[i] = 0; return; }
        }

        public static bool ShouldQueueBridge(RegionNavProfile profile, Vector3 position, Vector3 nextCorner,
            float queuedSeconds, float now, BridgeAdmission own, out NavRegionVolume bridge)
        {
            bridge = profile != null ? profile.FindBridgeMouth(position, nextCorner) : null;
            if (bridge == null || HasBridgeAdmission(profile, bridge, now, own)) return false;
            bool full = BridgeLoad(profile, bridge, now) >= bridge.capacity;
            if (full && queuedSeconds <= 0f) QueueEvents++;
            return full;
        }

        public static bool BridgeOverflowAllows(RegionNavProfile profile, NavRegionVolume bridge, int load)
        {
            if (profile == null || bridge == null || bridge.kind != NavRegionKind.Bridge || !bridge.IsChokepoint) return false;
            int bound = Mathf.Min(Mathf.Max(1, profile.BridgeHardMax), bridge.capacity + Mathf.Max(0, profile.BridgeOverflowBodies));
            return load < bound;
        }

        public static bool TryBridgeTimedOutRelease(RegionNavProfile profile, NavRegionVolume bridge, float queuedSeconds, float now, out BridgeAdmission admission)
        {
            admission = default;
            if (profile == null || queuedSeconds < Mathf.Max(0f, profile.BridgeQueueReleaseSeconds) || !PrepareBridgeClock(bridge, now)
                || now < bridge.BridgeNextRelease || !BridgeOverflowAllows(profile, bridge, BridgeLoad(profile, bridge, now))) return false;
            if (!NoteBridgeAdmission(profile, bridge, now, out admission)) return false;
            bridge.BridgeNextRelease = now + Mathf.Max(0.1f, profile.BridgeAdmitHold);
            return true;
        }

        /// <summary>Timeout comparison for ONE waiting Bridge. Generic ChooseRoute and other volume choices are untouched.</summary>
        public static int ChooseBridgeTimeoutRoute(RegionNavProfile profile, NavRegionVolume bridge, Vector3 position, Vector3 goal,
            Vector3[] buffer, int directCount, out bool rerouted)
        {
            rerouted = false;
            if (profile == null || !profile.BridgeTimeoutDetour || bridge == null || bridge.kind != NavRegionKind.Bridge || !bridge.hasAlternative
                || buffer == null || directCount < 2 || directCount > buffer.Length
                || !AlternativeAhead(bridge, position, goal, profile.AlternativeAheadMargin)
                || !BridgePathIntersects(bridge, buffer, directCount)) return directCount;
            float direct = EffectiveCost(profile, buffer, directCount, out _);
            AlternativeQuery query = AlternativeQueryOverride ?? TryAlternative;
            int n = query(profile, bridge, position, goal, s_altScratch, out float altCost);
            if (n < 2 || n > buffer.Length || n > s_altScratch.Length || altCost < 0f || float.IsNaN(altCost) || float.IsInfinity(altCost)) return directCount;
            // A truncated or partial path must never become a timeout route. TryAlternative already rejects non-complete legs.
            if (FlatSquared(s_altScratch[0], position) > 0.25f || FlatSquared(s_altScratch[n - 1], goal) > 0.25f
                || BridgePathIntersects(bridge, s_altScratch, n)) return directCount;
            for (int i = 0; i < n; i++)
                if (float.IsNaN(s_altScratch[i].x) || float.IsInfinity(s_altScratch[i].x)
                    || float.IsNaN(s_altScratch[i].y) || float.IsInfinity(s_altScratch[i].y)
                    || float.IsNaN(s_altScratch[i].z) || float.IsInfinity(s_altScratch[i].z)) return directCount;
            if (altCost >= direct * (1f + Mathf.Clamp01(profile.RouteBiasSpread))) return directCount;
            for (int i = 0; i < n; i++) buffer[i] = s_altScratch[i];
            rerouted = true;
            RerouteCount++;
            return n;
        }

        private static float FlatSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }
    }
}
