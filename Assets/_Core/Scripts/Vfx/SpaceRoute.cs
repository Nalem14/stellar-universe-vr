using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>A body a flight path keeps clear of (star, planet with its ring), in world space.</summary>
    public readonly struct RouteObstacle
    {
        public readonly Transform Body;
        public readonly float Radius;

        public RouteObstacle(Transform body, float radius)
        {
            Body = body;
            Radius = radius;
        }
    }

    /// <summary>
    /// A flight path through the system exterior that goes round the star and the planets instead of through
    /// them. Straight when the line is clear (no allocation, exact lerp); otherwise detour waypoints are pushed
    /// out beside each body in the way — on the ecliptic side the line already leans to — and joined by a
    /// Catmull-Rom curve, sampled once and walked by arc length (so a ship keeps its pace round the bend).
    /// Built once per move, never per frame.
    /// </summary>
    public sealed class SpaceRoute
    {
        const int Samples = 40;
        const int MaxDetours = 4;
        /// <summary>How far outside a body's keep-out radius a detour waypoint sits (the curve cuts inside a little).</summary>
        const float WaypointMargin = 1.3f;

        readonly Vector3 _a;
        readonly Vector3 _b;
        readonly Vector3[] _pts;
        readonly float[] _len;

        public bool Straight => _pts == null;

        SpaceRoute(Vector3 a, Vector3 b, Vector3[] pts, float[] len)
        {
            _a = a;
            _b = b;
            _pts = pts;
            _len = len;
        }

        public static SpaceRoute Line(Vector3 a, Vector3 b) => new(a, b, null, null);

        public static SpaceRoute Plan(Vector3 a, Vector3 b, IReadOnlyList<RouteObstacle> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0 || (b - a).sqrMagnitude < 1f)
                return Line(a, b);

            var way = new List<Vector3>(4) { a, b };
            for (var pass = 0; pass < MaxDetours; pass++)
            {
                var inserted = false;
                for (var i = 0; i < way.Count - 1 && !inserted; i++)
                {
                    if (!Blocked(way[i], way[i + 1], obstacles, out var c, out var r))
                        continue;
                    way.Insert(i + 1, Detour(way[i], way[i + 1], c, r));
                    inserted = true;
                }

                if (!inserted)
                    break;
            }

            if (way.Count == 2)
                return Line(a, b);

            var pts = new Vector3[Samples + 1];
            var len = new float[Samples + 1];
            var segs = way.Count - 1;
            for (var s = 0; s <= Samples; s++)
            {
                var f = s / (float)Samples * segs;
                var i = Mathf.Min(segs - 1, Mathf.FloorToInt(f));
                var t = f - i;
                var p0 = way[Mathf.Max(0, i - 1)];
                var p1 = way[i];
                var p2 = way[i + 1];
                var p3 = way[Mathf.Min(way.Count - 1, i + 2)];
                pts[s] = CatmullRom(p0, p1, p2, p3, t);
                len[s] = s == 0 ? 0f : len[s - 1] + Vector3.Distance(pts[s - 1], pts[s]);
            }

            return new SpaceRoute(a, b, pts, len);
        }

        /// <summary>Position at <paramref name="u"/> ∈ [0,1] of the way (by distance flown).</summary>
        public Vector3 At(float u)
        {
            u = Mathf.Clamp01(u);
            if (_pts == null)
                return Vector3.LerpUnclamped(_a, _b, u);
            var want = u * _len[Samples];
            var lo = 0;
            var hi = Samples;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (_len[mid] < want)
                    lo = mid;
                else
                    hi = mid;
            }

            var span = _len[hi] - _len[lo];
            return Vector3.Lerp(_pts[lo], _pts[hi], span > 1e-4f ? (want - _len[lo]) / span : 0f);
        }

        /// <summary>Direction of flight at <paramref name="u"/> (unit; the chord when the route is straight).</summary>
        public Vector3 Heading(float u)
        {
            if (_pts == null)
            {
                var d = _b - _a;
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            }

            var h = At(Mathf.Min(1f, u + 0.02f)) - At(Mathf.Max(0f, u - 0.02f));
            return h.sqrMagnitude > 1e-6f ? h.normalized : (_b - _a).normalized;
        }

        /// <summary>The body the segment comes nearest to inside its keep-out radius (endpoints inside a body don't count it).</summary>
        static bool Blocked(Vector3 a, Vector3 b, IReadOnlyList<RouteObstacle> obstacles, out Vector3 centre, out float radius)
        {
            centre = default;
            radius = 0f;
            var worst = 0f;
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-4f)
                return false;
            for (var i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                if (o.Body == null)
                    continue;
                var c = o.Body.position;
                var r = o.Radius;
                // A ship berthed or starting inside a keep-out zone (close orbit) leaves it the way it came.
                if ((a - c).sqrMagnitude < r * r || (b - c).sqrMagnitude < r * r)
                    continue;
                var t = Mathf.Clamp01(Vector3.Dot(c - a, ab) / len2);
                var d = Vector3.Distance(a + ab * t, c);
                var depth = (r - d) / r;
                if (depth > worst)
                {
                    worst = depth;
                    centre = c;
                    radius = r;
                }
            }

            return worst > 0f;
        }

        static Vector3 Detour(Vector3 a, Vector3 b, Vector3 c, float r)
        {
            var ab = (b - a).normalized;
            var closest = a + ab * Vector3.Dot(c - a, ab);
            var off = closest - c;
            // Dead centre: go round on the ecliptic (the side the line leans to, else starboard).
            if (off.sqrMagnitude < 1f)
            {
                off = Vector3.Cross(Vector3.up, ab);
                if (off.sqrMagnitude < 1e-4f)
                    off = Vector3.right;
            }

            off -= ab * Vector3.Dot(off, ab);
            return c + off.normalized * (r * WaypointMargin);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
