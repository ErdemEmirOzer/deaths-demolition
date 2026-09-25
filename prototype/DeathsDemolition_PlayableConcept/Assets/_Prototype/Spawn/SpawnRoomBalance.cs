using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// WP-076 B6c · §5.3 threat routes in a v6 room (S-05's NavRegionMarker rooms and their exit gates). While the player is
    /// inside a Room (or in one of its exit gates): every spawn point is assigned the room exit nearest to it; inner gates
    /// weigh ×1.2 (pressure) and edge gates ×0.9; the last three spawns' exits are remembered — when one exit took more
    /// than 70 % of them the points arriving through the OTHER exits weigh ×1.5 (DoorSideBalance: the room is entered from
    /// both sides). Outside a room (or outside the v6 profile) every factor is 1. Scratch buffers only, 0 allocation.
    /// </summary>
    public static class SpawnRoomBalance
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.3: inner / edge factor while the player is in a room; door-side memory (spawns), share over which the other exits are favoured, their factor.", true)]
        public const float InnerInRoom = 1.2f, EdgeInRoom = 0.9f, DominantShare = 0.7f, OtherExitFactor = 1.5f;
        public const int Memory = 3;

        private static readonly List<NavRegionVolume> s_exits = new List<NavRegionVolume>(8);
        private static readonly int[] s_recent = new int[Memory];
        private static int s_recentCount, s_recentHead;
        private static string s_room = "";

        /// <summary>The room the player stands in ("" = none) after the last <see cref="Prepare"/>.</summary>
        public static string CurrentRoom => s_room;
        public static int ExitCount => s_exits.Count;

        /// <summary>Sets RoomFactor / RoomExitIndex on every point for this draw. Returns true when the player is in a room.</summary>
        public static bool Prepare(IReadOnlyList<SpawnPointRuntime> points, Vector3 playerPosition, bool v6)
        {
            string room = v6 ? RoomOf(playerPosition) : "";
            if (room != s_room) { s_room = room; s_recentCount = 0; s_recentHead = 0; }
            s_exits.Clear();
            if (room.Length > 0) RegionNavProfile.CollectExits(room, s_exits);

            int dominant = DominantExit();
            for (int i = 0; i < points.Count; i++)
            {
                SpawnPointRuntime p = points[i];
                if (p == null) continue;
                if (room.Length == 0 || s_exits.Count == 0) { p.RoomFactor = 1f; p.RoomExitIndex = -1; continue; }

                int exit = NearestExit(p.NavPosition);
                p.RoomExitIndex = exit;
                float f = p.Definition != null && p.Definition.Kind == SpawnPointKind.Inner ? InnerInRoom : EdgeInRoom;
                if (dominant >= 0 && exit != dominant) f *= OtherExitFactor;
                p.RoomFactor = f;
            }
            return room.Length > 0 && s_exits.Count > 0;
        }

        /// <summary>A body spawned from <paramref name="point"/>: remember its exit while the player is in the room.</summary>
        public static void Record(SpawnPointRuntime point)
        {
            if (s_room.Length == 0 || point == null || point.RoomExitIndex < 0) return;
            s_recent[s_recentHead] = point.RoomExitIndex;
            s_recentHead = (s_recentHead + 1) % Memory;
            if (s_recentCount < Memory) s_recentCount++;
        }

        /// <summary>The exit that took more than 70 % of the last three spawns (−1 = none or not enough history).</summary>
        public static int DominantExit()
        {
            if (s_recentCount < Memory) return -1;
            for (int i = 0; i < s_recentCount; i++)
            {
                int same = 0;
                for (int j = 0; j < s_recentCount; j++) if (s_recent[j] == s_recent[i]) same++;
                if ((float)same / s_recentCount > DominantShare) return s_recent[i];
            }
            return -1;
        }

        private static string RoomOf(Vector3 p)
        {
            NavRegionVolume v = RegionNavProfile.Active.Find(p);
            if (v == null) return "";
            if (v.kind == NavRegionKind.Room) return v.id ?? "";
            if (v.kind == NavRegionKind.Gate && !string.IsNullOrEmpty(v.roomId)) return v.roomId;   // standing in a doorway = in that room
            return "";
        }

        private static int NearestExit(Vector3 p)
        {
            int best = -1; float bestSq = float.MaxValue;
            for (int i = 0; i < s_exits.Count; i++)
            {
                Vector3 d = s_exits[i].center - p; d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        public static void ResetStatics() { s_exits.Clear(); s_recentCount = 0; s_recentHead = 0; s_room = ""; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain() => ResetStatics();
    }
}
