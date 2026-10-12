using Unity.Profiling;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4-2 (S-07) · B9 the bending seeker trail, pure. S-02 keeps the path of a round that locked a body at spawn (sh_cap Seeker
    /// Swarm's extra volleys: <c>ProjectileSystem.TryGetPath</c>, the kept points oldest first, then where the round is now, one point
    /// every <c>PathStepSeconds</c> of flight). TracerRibbonRenderer lays that record's top-down streak along the path instead of
    /// straight back from its head: the same streak — its core, halo, body or pellet bar with the run's look and the B4-1 ladder —
    /// as long as its kept path, at most 10 m (<see cref="SeekLength"/>; S-07 12.10 decision (a): a one-exposure streak, ≈ 1.6 m on
    /// an AR extra volley, showed the bend ≈ 0.4 frame per joint), × the tail-out on impact, as consecutive DD/VFX Ribbon instances,
    /// one per path segment (≤ <c>ProjectileSystem.PathPoints</c> a record).
    /// <list type="bullet">
    /// <item><see cref="Lay"/> walks back from the head: a point closer than <see cref="MinSegmentMetres"/> to the segment's front is
    /// merged into the chord to the next older one (DD/VFX Ribbon draws a segment under 4 px on screen as its end-on dot: a blob mid-
    /// trail), the trail ends inside the chord where its length runs out, never past the oldest kept point (the muzzle while the path is
    /// short), and a last tail piece shorter than the minimum is dropped (its α is the tail ramp's ≈ 0).</item>
    /// <item>Each segment carries its distances along the trail from the trail's tail end (<see cref="Segment.TailAt"/> /
    /// <see cref="Segment.HeadAt"/>): DD/VFX Ribbon reads t = distance / the trail's length when <c>_Span.w &lt; 0</c> (−length), so the
    /// head bead, the exponential tail and the head → tail colour run once over the whole bent streak, not once per segment.</item>
    /// </list>
    /// A round that flies straight (no lock) never reaches here: WP-101 / B4-1 byte-identical. 0 B (caller-owned arrays).
    /// </summary>
    public static class TreeSeekTrail
    {
        /// <summary>The bending trail's own marker (TracerRibbonRenderer.TickTd path read + DrawTd / DrawTdInk segment lay-out).</summary>
        public static readonly ProfilerMarker Marker = new ProfilerMarker("WP104.Vfx.SeekTrail");

        /// <summary>
        /// A path segment shorter than this is merged into the chord to the next older point: DD/VFX Ribbon draws a segment under 4 px
        /// on screen as its end-on dot (here ≈ 11 px at the TD camera's 95 px/m). The same floor drops a hair of a tail end.
        /// </summary>
        public const float MinSegmentMetres = .12f;

        /// <summary>
        /// S-07 12.10 B9 decision (a): a seeking round's streak length before the impact's tail-out — its whole kept path
        /// (<paramref name="arc"/>, <see cref="ArcLength"/>) up to <see cref="TreeVfxLadder.TrailMaxMetres"/> (10 m, the ceiling the Shot
        /// ladder's trail shares). The sniper vapour keeps the whole path; a straight round keeps WP-101's one-exposure trail.
        /// </summary>
        public static float SeekLength(float arc) => Mathf.Clamp(arc, 0f, TreeVfxLadder.TrailMaxMetres);

        /// <summary>One laid segment, head end first (index 0 = at the round's head).</summary>
        public struct Segment
        {
            public Vector3 Head, Tail;
            /// <summary>Distances (m) along the trail from its tail end to this segment's head end / tail end (DD/VFX Ribbon
            /// <c>_Span.y</c> / <c>_Span.x</c>); the trail's head is at its full length, its tail end at 0.</summary>
            public float HeadAt, TailAt;
        }

        /// <summary>
        /// The path's length (m) as <see cref="Lay"/> walks it from <paramref name="head"/> back to the oldest of the
        /// <paramref name="count"/> kept <paramref name="points"/> (oldest first): the chords between the points it keeps.
        /// </summary>
        public static float ArcLength(Vector3[] points, int count, Vector3 head)
        {
            if (points == null || count <= 0) return 0f;
            count = Mathf.Min(count, points.Length);
            Vector3 front = head;
            float sum = 0f;
            for (int j = count - 1; j >= 0; j--)
            {
                float seg = (points[j] - front).magnitude;
                if (seg < MinSegmentMetres && j > 0) continue;   // merged into the chord to the next older point
                sum += seg;
                front = points[j];
            }

            return sum;
        }

        /// <summary>
        /// Lays a streak of <paramref name="length"/> m back from <paramref name="head"/> along the kept <paramref name="points"/>
        /// (oldest first, <paramref name="count"/> of them) into <paramref name="into"/>, head end first; at most
        /// <paramref name="maxSegments"/>. The last segment a record may take carries what is left of the length straight on along its
        /// chord (a budget cut never shortens the streak); the oldest point is never passed. Returns the segments laid (0: nothing to
        /// lay — the caller keeps its straight streak).
        /// </summary>
        public static int Lay(Vector3[] points, int count, Vector3 head, float length, int maxSegments, Segment[] into)
        {
            if (points == null || into == null || count <= 0 || length <= 0f) return 0;
            int max = Mathf.Min(maxSegments, into.Length);
            if (max <= 0) return 0;
            count = Mathf.Min(count, points.Length);
            Vector3 front = head;
            float left = length;
            int n = 0;
            for (int j = count - 1; j >= 0; j--)
            {
                Vector3 d = points[j] - front;
                float seg = d.magnitude;
                bool oldest = j == 0;
                if (seg < MinSegmentMetres && !oldest) continue;   // merged: the chord to the next older point
                if (seg < 1e-5f) break;                            // the oldest point sits on the front: the path ends here
                bool last = n == max - 1;
                // the streak ends inside this chord, at the path's oldest point, or (the record's last segment) straight on along it
                float use = last && !oldest ? left : Mathf.Min(left, seg);
                if (n > 0 && use < MinSegmentMetres) break;        // a hair of a tail end: dropped
                into[n++] = new Segment { Head = front, Tail = front + d * (use / seg), HeadAt = left, TailAt = left - use };
                left -= use;
                if (last || left <= 1e-5f) break;
                front = points[j];
            }

            return n;
        }
    }
}
