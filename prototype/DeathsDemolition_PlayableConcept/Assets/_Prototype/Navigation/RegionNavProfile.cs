using System;
using DD.Prototype.Core.Config;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.Navigation
{
    public enum NavRegionKind
    {
        Open = 0,
        Bridge = 1,     // narrow single-file crossing (REQ-046/047: the koi pond bridge, shore path as the alternative)
        Corridor = 2,   // roofed bamboo corridor with two exits (REQ-111: ≥ 2 connections)
        Gate = 3,       // palace / service gate throat (AP-005C GATE-N 5.2 m; west service gate)
        Room = 4,       // WP-076 B6 §5.2: a v6 room R1–R7 (capacity = floor ÷ 2.5 m², cap 8; exits = its Gate volumes, roomId)
    }

    /// <summary>
    /// WP-030 (ADR-030-1) · One regional navigation volume: a world-space XZ box the NavMesh
    /// crosses (bridge, corridor, gate throat), its route preference and its throughput.
    /// * <see cref="costMultiplier"/> ≥ 1 scales the path length of segments inside the volume
    ///   when routes are compared - it diversifies routes (REQ-112), it never closes one.
    /// * <see cref="capacity"/> > 0 turns the volume into a chokepoint: a member about to enter
    ///   while that many members are already inside queues at the mouth for a bounded time
    ///   (DEC-032: the pressure stays, the pile-up does not).
    /// * <see cref="alternative"/> (optional) is a waypoint that routes around the volume - the
    ///   shore path beside the bridge, the other palace gate - offered to a member whose
    ///   direct route crosses the volume.
    /// * <see cref="areaName"/> (optional) names a NavMesh area; when the bake carries it the
    ///   cost is ALSO set on the NavMesh itself (NavMesh.SetAreaCost, post-bake assignment).
    ///   The World_AP005 bake is single-area today, so the router's route comparison is what
    ///   runs; S-05 can add NavMeshModifierVolumes with these names without touching this code.
    /// </summary>
    [Serializable]
    public sealed class NavRegionVolume
    {
        public string id = "";
        public NavRegionKind kind = NavRegionKind.Open;
        public Vector3 center = Vector3.zero;
        public Vector3 size = new Vector3(4f, 6f, 4f);
        [PrototypeOnly(PendingDecisions.None, "Route-preference multiplier for path segments inside the volume (1 = neutral; > 1 avoided when an alternative is cheaper).", true)]
        [Range(1f, 4f)] public float costMultiplier = 1f;
        [PrototypeOnly(PendingDecisions.None, "Members inside before newcomers queue at the mouth (0 = never queue).", true)]
        [Range(0, 40)] public int capacity = 0;
        public bool hasAlternative = false;
        public Vector3 alternative = Vector3.zero;
        public string areaName = "";
        /// <summary>WP-076 B6: for a Gate that is a room's exit, the Room volume's id ("" = not a room exit).</summary>
        public string roomId = "";
        /// <summary>WP-076 B6: optional world XZ outline (≥ 3 points; regions-v6 polygons); empty = the box.</summary>
        public Vector2[] polygonXZ;

        // Bridge-only, prewarmed at profile/scene registration; no allocations from queue ticks.
        internal const int BridgeAdmissionSlots = 64;
        [NonSerialized] internal int[] BridgeAdmissionIds;
        [NonSerialized] internal float[] BridgeAdmissionTimes;
        [NonSerialized] internal int BridgeAdmissionCursor, BridgeNextAdmissionId;
        [NonSerialized] internal float BridgeNextRelease, BridgeLastClock = float.NegativeInfinity;

        internal void PrewarmBridgeState()
        {
            if (kind != NavRegionKind.Bridge) return;
            if (BridgeAdmissionIds == null) BridgeAdmissionIds = new int[BridgeAdmissionSlots];
            if (BridgeAdmissionTimes == null) BridgeAdmissionTimes = new float[BridgeAdmissionSlots];
            ClearBridgeState();
        }

        internal void ClearBridgeState()
        {
            if (BridgeAdmissionIds != null) Array.Clear(BridgeAdmissionIds, 0, BridgeAdmissionIds.Length);
            if (BridgeAdmissionTimes != null) Array.Clear(BridgeAdmissionTimes, 0, BridgeAdmissionTimes.Length);
            BridgeAdmissionCursor = 0;
            BridgeNextRelease = 0f;
            BridgeLastClock = float.NegativeInfinity;
            // Keep the identity sequence: an old pooled follower cannot own a newly issued slot after a clock reset.
        }

        public bool Contains(Vector3 p)
        {
            if (polygonXZ != null && polygonXZ.Length >= 3) return InsidePolygon(polygonXZ, p.x, p.z);
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            return p.x >= center.x - hx && p.x <= center.x + hx && p.z >= center.z - hz && p.z <= center.z + hz;
        }

        private static bool InsidePolygon(Vector2[] poly, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > z) != (poly[j].y > z) && x < (poly[j].x - poly[i].x) * (z - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            }
            return inside;
        }

        public bool IsChokepoint => capacity > 0;
        public bool IsCostly => costMultiplier > 1.0001f;
    }

    /// <summary>The regional navigation profile of the ring map. <see cref="Active"/> = Resources asset when present, else the code defaults.</summary>
    public sealed class RegionNavProfile : ScriptableObject
    {
        public const string ResourcesPath = "DD_RegionNav";

        [SerializeField] private NavRegionVolume[] _volumes = new NavRegionVolume[0];

        [PrototypeOnly(PendingDecisions.None, "Seconds a member queues at a full chokepoint before it pushes in anyway (never a deadlock).", true)]
        [Range(0f, 10f)] public float MaxQueueSeconds = 2.5f;

        [PrototypeOnly(PendingDecisions.None, "Bridge-only wait before trying the target bridge alternative or a bounded admission.", true)]
        [Range(0f, 10f)] public float BridgeQueueReleaseSeconds = 1.5f;
        [PrototypeOnly(PendingDecisions.None, "Bridge-only timeout may query that bridge alternative; ordinary routes keep their seeded choice.", true)]
        public bool BridgeTimeoutDetour = true;
        [PrototypeOnly(PendingDecisions.None, "Bridge mouth look-ahead along the next path segment, metres.", true)]
        [Range(0f, 5f)] public float BridgeMouthProbe = 1.5f;
        [PrototypeOnly(PendingDecisions.None, "Bridge-only admission lifetime and minimum interval between overflow releases, seconds.", true)]
        [Range(0.1f, 5f)] public float BridgeAdmitHold = 1.2f;
        [PrototypeOnly(PendingDecisions.None, "Bridge-only extra admissions after timeout, still limited by BridgeHardMax.", true)]
        [Range(0, 4)] public int BridgeOverflowBodies = 2;
        [PrototypeOnly(PendingDecisions.None, "Bridge-only hard upper bound on occupancy plus live admissions.", true)]
        [Range(1, 40)] public int BridgeHardMax = 10;

        [PrototypeOnly(PendingDecisions.None, "Speed factor while queued at a chokepoint mouth (0 = stop, keeps the DEC-032 threat visible).", true)]
        [Range(0f, 1f)] public float QueueSpeedFactor = 0.15f;

        [PrototypeOnly(PendingDecisions.None, "Route diversification: the member's route roll (0..1 from its spawn seed) widens the alternative's acceptance by this fraction of the direct cost.", true)]
        [Range(0f, 1f)] public float RouteBiasSpread = 0.25f;

        [PrototypeOnly(PendingDecisions.None, "WP-088: metres the alternative's waypoint must lie closer to the goal than the member does to still count as ahead (no detour back to a waypoint already reached).", true)]
        [Range(0f, 5f)] public float AlternativeAheadMargin = 1f;

        [PrototypeOnly(PendingDecisions.None, "Crowd separation radius scale per AP-007 body fit (A slim / B / C broad); the collision capsule is not scaled.", true)]
        public float FitRadiusScaleA = 0.92f, FitRadiusScaleB = 1f, FitRadiusScaleC = 1.12f;

        private static RegionNavProfile s_active;

        public static RegionNavProfile Active
        {
            get
            {
                if (s_active == null)
                {
                    s_active = Resources.Load<RegionNavProfile>(ResourcesPath);
                    if (s_active == null)
                    {
                        s_active = CreateDefault();
                        s_active.hideFlags = HideFlags.HideAndDontSave;
                    }
                }

                return s_active;
            }
        }

        private void OnEnable() => PrewarmBridgeState();

        private void PrewarmBridgeState()
        {
            if (_volumes == null) return;
            for (int i = 0; i < _volumes.Length; i++) _volumes[i]?.PrewarmBridgeState();
        }

        public static void SetActive(RegionNavProfile profile)
        {
            s_active = profile;
            if (profile != null) profile.PrewarmBridgeState();
        }
        public static void ResetStatics()
        {
            ResetBridgeRuntimeState();
            s_active = null;
        }

        internal static void ResetBridgeRuntimeState()
        {
            if (s_active != null && s_active._volumes != null)
                for (int i = 0; i < s_active._volumes.Length; i++) s_active._volumes[i]?.ClearBridgeState();
            for (int i = 0; i < s_scene.Count; i++) s_scene[i]?.ClearBridgeState();
        }

        public NavRegionVolume[] Volumes => _volumes;
        public void Set(NavRegionVolume[] volumes)
        {
            _volumes = volumes ?? new NavRegionVolume[0];
            PrewarmBridgeState();
        }

        public float FitRadiusScale(string fit)
        {
            switch (fit)
            {
                case "A": return FitRadiusScaleA;
                case "C": return FitRadiusScaleC;
                default: return FitRadiusScaleB;
            }
        }

        // WP-076 B6: volumes placed in the loaded world scene (NavRegionMarker). When any exist they describe that world
        // and replace the profile's own list (the profile's coordinates are World_AP005's).
        private static readonly System.Collections.Generic.List<NavRegionVolume> s_scene = new System.Collections.Generic.List<NavRegionVolume>(32);
        public static int SceneVolumeCount => s_scene.Count;
        public static void RegisterSceneVolume(NavRegionVolume v)
        {
            if (v == null || s_scene.Contains(v)) return;
            v.PrewarmBridgeState();
            s_scene.Add(v);
        }
        public static void UnregisterSceneVolume(NavRegionVolume v) { s_scene.Remove(v); }

        /// <summary>WP-076 B6c: the scene Gate volumes that open room <paramref name="roomId"/> (appended; caller clears). 0 allocation.</summary>
        public static int CollectExits(string roomId, System.Collections.Generic.List<NavRegionVolume> into)
        {
            int n = 0;
            if (into == null || string.IsNullOrEmpty(roomId)) return 0;
            for (int i = 0; i < s_scene.Count; i++)
            {
                NavRegionVolume v = s_scene[i];
                if (v != null && v.kind == NavRegionKind.Gate && v.roomId == roomId) { into.Add(v); n++; }
            }
            return n;
        }

        /// <summary>The volume containing <paramref name="p"/>, or null. Allocation-free. Rooms lose to the gates/throats inside them.</summary>
        public NavRegionVolume Find(Vector3 p)
        {
            if (s_scene.Count > 0)
            {
                NavRegionVolume room = null;
                for (int i = 0; i < s_scene.Count; i++)
                {
                    NavRegionVolume v = s_scene[i];
                    if (v == null || !v.Contains(p)) continue;
                    if (v.kind != NavRegionKind.Room) return v;
                    if (room == null) room = v;
                }
                return room;
            }

            for (int i = 0; i < _volumes.Length; i++)
            {
                if (_volumes[i] != null && _volumes[i].Contains(p)) return _volumes[i];
            }

            return null;
        }

        /// <summary>
        /// WP-096 R3 (5b4118c5; gt S2 EditMode test): the Bridge mouth that meters THIS body's crossing — none when its goal stands on the
        /// deck itself (the AlternativeAhead rule's "deck is the destination"): that body walks on to it under the ordinary WP-030 queue
        /// (son10b V6 Waterfall: bodies held still at both mouths with the target on the C8 deck).
        /// </summary>
        public NavRegionVolume FindBridgeMouth(Vector3 position, Vector3 nextCorner, Vector3 goal)
        {
            NavRegionVolume bridge = FindBridgeMouth(position, nextCorner);
            return bridge != null && bridge.Contains(goal) ? null : bridge;
        }

        /// <summary>Only a Bridge mouth on the next segment; a corner beyond the deck is supported.</summary>
        public NavRegionVolume FindBridgeMouth(Vector3 position, Vector3 nextCorner)
        {
            if (s_scene.Count > 0)
            {
                for (int i = 0; i < s_scene.Count; i++)
                    if (RegionNavRouter.AtBridgeMouth(s_scene[i], position, nextCorner, BridgeMouthProbe)) return s_scene[i];
                return null;
            }
            for (int i = 0; i < _volumes.Length; i++)
                if (RegionNavRouter.AtBridgeMouth(_volumes[i], position, nextCorner, BridgeMouthProbe)) return _volumes[i];
            return null;
        }

        /// <summary>
        /// Post-bake assignment: set NavMesh area costs for volumes whose <c>areaName</c> exists in
        /// the project's NavMesh areas (not Walkable / Not Walkable). Returns how many were set.
        /// With a single-area bake this is 0 and the router's route comparison carries the rule.
        /// </summary>
        public int ApplyAreaCosts()
        {
            int applied = 0;
            for (int i = 0; i < _volumes.Length; i++)
            {
                NavRegionVolume v = _volumes[i];
                if (v == null || string.IsNullOrEmpty(v.areaName)) continue;
                int area = NavMesh.GetAreaFromName(v.areaName);
                if (area <= 1) continue;   // -1 unknown, 0 Walkable, 1 Not Walkable
                NavMesh.SetAreaCost(area, Mathf.Max(1f, v.costMultiplier));
                applied++;
            }

            return applied;
        }

        /// <summary>
        /// The ring map's volumes (World_AP005 / procedural layout share the WorldLayout frame:
        /// +Z north, +X east). Coordinates from <c>WorldLayout</c> / <c>DefaultSpawnPointLayout</c>;
        /// PROTOTYPE-ONLY sizes and numbers. Alternatives are projected onto the NavMesh at
        /// use; one that does not project is simply not offered.
        /// </summary>
        public static RegionNavProfile CreateDefault()
        {
            var p = CreateInstance<RegionNavProfile>();
            p.name = "DD_RegionNav (code defaults)";
            p._volumes = new[]
            {
                // South koi-pond bridge: x = 4, z -18 … -39.5, deck 3 m wide in the procedural map, 1.2 m single file in AP-005 (WP-014).
                new NavRegionVolume { id = "S_Bridge", kind = NavRegionKind.Bridge, center = new Vector3(4f, 0f, -28.75f), size = new Vector3(5f, 6f, 23f), costMultiplier = 1.35f, capacity = 2, hasAlternative = true, alternative = new Vector3(18f, 0f, -30f), areaName = "Bridge" },   // WP-054: 1.6 → 1.35 on the v3 NavMesh (baked Bridge area cost already biases the shore; 1.6 sent every body round — 0 queue events / 47 reroutes in WP-052)
                // West bamboo corridor: x -36 … -30, z -12 … 12 (roofed; ≥ 2 exits - REQ-111). Alternative: the open ground east of the corridor.
                new NavRegionVolume { id = "W_Corridor", kind = NavRegionKind.Corridor, center = new Vector3(-33f, 0f, 0f), size = new Vector3(7f, 6f, 25f), costMultiplier = 1.3f, capacity = 5, hasAlternative = true, alternative = new Vector3(-24f, 0f, 0f), areaName = "Corridor" },
                // Palace front gates (north): west and east throats; each is the other's alternative.
                new NavRegionVolume { id = "N_GateWest", kind = NavRegionKind.Gate, center = new Vector3(-8f, 0f, 34f), size = new Vector3(6f, 6f, 6f), costMultiplier = 1f, capacity = 3, hasAlternative = true, alternative = new Vector3(8f, 0f, 31f), areaName = "Gate" },
                new NavRegionVolume { id = "N_GateEast", kind = NavRegionKind.Gate, center = new Vector3(8f, 0f, 34f), size = new Vector3(6f, 6f, 6f), costMultiplier = 1f, capacity = 3, hasAlternative = true, alternative = new Vector3(-8f, 0f, 31f), areaName = "Gate" },
                // North-west service gate.
                new NavRegionVolume { id = "NW_ServiceGate", kind = NavRegionKind.Gate, center = new Vector3(-26f, 0f, 25f), size = new Vector3(5f, 6f, 5f), costMultiplier = 1.15f, capacity = 2, hasAlternative = true, alternative = new Vector3(-8f, 0f, 31f), areaName = "Gate" },
            };
            p.PrewarmBridgeState();
            return p;
        }
    }
}
