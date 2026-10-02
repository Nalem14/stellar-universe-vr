using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Smooth surfaces of revolution about a vertical axis (rotunda walls, domes, rings, floor discs), plus the
    /// radial pieces that go with them (ribs, mullions, end caps). Angles in degrees from +z toward +x; profile
    /// points are (radius, height). Normals: the profile's left-hand side as walked (<c>(t.y, −t.x)</c>), flipped
    /// with <c>inward</c> — so a room profile walked floor → wall → ceiling faces into the room with
    /// <c>inward = true</c>, and a solid walked back → top → front faces out of it with <c>inward = false</c>.
    /// UVs in metres (arc length × profile length), planar xz, or normalised over the band. AO in vertex colour.
    /// </summary>
    public sealed class LatheMesh
    {
        public enum Uv
        {
            Metres,
            Planar,
            Normalised
        }

        readonly List<Vector3> _v = new();
        readonly List<Vector3> _n = new();
        readonly List<Vector2> _uv = new();
        readonly List<Color> _c = new();
        readonly List<int> _t = new();

        public Vector3 Centre;
        /// <summary>Largest step between two meridians (degrees).</summary>
        public float Step = 3.75f;

        public LatheMesh(Vector3 centre) => Centre = centre;

        public static Vector3 Dir(float deg)
        {
            var a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        public Vector3 At(float deg, float r, float y) => Centre + Dir(deg) * r + Vector3.up * y;

        public void Revolve(Vector2[] profile, float a0, float a1, bool inward, Uv uv = Uv.Metres, float[] ao = null)
        {
            if (profile == null || profile.Length < 2 || a1 <= a0)
                return;
            var segs = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / Step));
            var count = profile.Length;
            var n2 = new Vector2[count];
            var len = new float[count];
            for (var j = 0; j < count; j++)
            {
                var prev = profile[Mathf.Max(0, j - 1)];
                var next = profile[Mathf.Min(count - 1, j + 1)];
                var t = (next - prev).normalized;
                var n = new Vector2(t.y, -t.x);
                n2[j] = inward ? -n : n;
                if (j > 0)
                    len[j] = len[j - 1] + Vector2.Distance(profile[j - 1], profile[j]);
            }

            var total = Mathf.Max(1e-4f, len[count - 1]);
            var start = _v.Count;
            for (var i = 0; i <= segs; i++)
            {
                var deg = Mathf.Lerp(a0, a1, i / (float)segs);
                var d = Dir(deg);
                for (var j = 0; j < count; j++)
                {
                    var p = profile[j];
                    var pos = Centre + d * p.x + Vector3.up * p.y;
                    _v.Add(pos);
                    _n.Add((d * n2[j].x + Vector3.up * n2[j].y).normalized);
                    _uv.Add(uv switch
                    {
                        Uv.Planar => new Vector2(pos.x, pos.z),
                        Uv.Normalised => new Vector2(i / (float)segs, len[j] / total),
                        _ => new Vector2(deg * Mathf.Deg2Rad * Mathf.Max(0.5f, p.x), len[j])
                    });
                    var a = ao != null && j < ao.Length ? ao[j] : 1f;
                    _c.Add(new Color(a, a, a, 1f));
                }
            }

            for (var i = 0; i < segs; i++)
            for (var j = 0; j < count - 1; j++)
            {
                var a = start + i * count + j;
                var b = start + (i + 1) * count + j;
                var face = Vector3.Cross(_v[b] - _v[a], _v[a + 1] - _v[a]);
                if (face.sqrMagnitude < 1e-12f)
                    face = Vector3.Cross(_v[b + 1] - _v[b], _v[a + 1] - _v[b]);
                var want = _n[a] + _n[b + 1];
                if (Vector3.Dot(face, want) >= 0f)
                    _t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                else
                    _t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        }

        /// <summary>Close a revolved solid at angle <paramref name="deg"/> (a fan over its profile), facing <paramref name="towardA1"/> or back.</summary>
        public void Cap(Vector2[] profile, float deg, bool towardA1)
        {
            var c = Vector2.zero;
            foreach (var p in profile)
                c += p;
            c /= profile.Length;
            var d = Dir(deg);
            var tangent = Vector3.Cross(Vector3.up, d);
            var n = towardA1 ? tangent : -tangent;
            var centre = Centre + d * c.x + Vector3.up * c.y;
            for (var j = 0; j < profile.Length; j++)
            {
                var p = profile[j];
                var q = profile[(j + 1) % profile.Length];
                Tri(centre, Centre + d * p.x + Vector3.up * p.y, Centre + d * q.x + Vector3.up * q.y, n, 0.85f);
            }
        }

        /// <summary>A strip standing proud of a revolved surface along its profile at angle <paramref name="deg"/> (a rib).</summary>
        public void Rib(Vector2[] path, float deg, float halfWidth, float depth, bool inward)
        {
            var d = Dir(deg);
            var side = Vector3.Cross(Vector3.up, d) * halfWidth;
            for (var j = 0; j < path.Length - 1; j++)
            {
                var a = path[j];
                var b = path[j + 1];
                var t = (b - a).normalized;
                var n2 = new Vector2(t.y, -t.x) * (inward ? -1f : 1f);
                var n = (d * n2.x + Vector3.up * n2.y).normalized;
                var pa = Centre + d * a.x + Vector3.up * a.y;
                var pb = Centre + d * b.x + Vector3.up * b.y;
                var fa = pa + n * depth;
                var fb = pb + n * depth;
                Quad(fa - side, fa + side, fb + side, fb - side, n, 1f);
                Quad(pa + side, fa + side, fb + side, pb + side, side, 0.7f);
                Quad(pa - side, fa - side, fb - side, pb - side, -side, 0.7f);
            }
        }

        /// <summary>A radial bar (mullion, jamb, post) from radius r0 to r1, height y0 → y1, at angle <paramref name="deg"/>.</summary>
        public void Bar(float deg, float r0, float r1, float y0, float y1, float halfWidth)
        {
            var d = Dir(deg);
            var s = Vector3.Cross(Vector3.up, d) * halfWidth;
            Vector3 P(float r, float y) => Centre + d * r + Vector3.up * y;
            var inner = -d;
            Quad(P(r0, y0) - s, P(r0, y0) + s, P(r0, y1) + s, P(r0, y1) - s, inner, 1f);
            Quad(P(r0, y0) + s, P(r1, y0) + s, P(r1, y1) + s, P(r0, y1) + s, s, 0.75f);
            Quad(P(r0, y0) - s, P(r1, y0) - s, P(r1, y1) - s, P(r0, y1) - s, -s, 0.75f);
            Quad(P(r0, y1) - s, P(r0, y1) + s, P(r1, y1) + s, P(r1, y1) - s, Vector3.up, 0.8f);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, float ao)
        {
            var face = Vector3.Cross(b - a, d - a);
            var flip = Vector3.Dot(face, hint) < 0f;
            var n = (flip ? -face : face).normalized;
            var i = _v.Count;
            _v.AddRange(new[] { a, b, c, d });
            _n.AddRange(new[] { n, n, n, n });
            var w = Vector3.Distance(a, b);
            var h = Vector3.Distance(a, d);
            _uv.AddRange(new[] { Vector2.zero, new Vector2(w, 0f), new Vector2(w, h), new Vector2(0f, h) });
            var col = new Color(ao, ao, ao, 1f);
            _c.AddRange(new[] { col, col, col, col });
            _t.AddRange(flip ? new[] { i, i + 3, i + 2, i, i + 2, i + 1 } : new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint, float ao)
        {
            var face = Vector3.Cross(b - a, c - a);
            var flip = Vector3.Dot(face, hint) < 0f;
            var n = (flip ? -face : face).normalized;
            var i = _v.Count;
            _v.AddRange(new[] { a, b, c });
            _n.AddRange(new[] { n, n, n });
            _uv.AddRange(new[] { new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z) });
            var col = new Color(ao, ao, ao, 1f);
            _c.AddRange(new[] { col, col, col });
            _t.AddRange(flip ? new[] { i, i + 2, i + 1 } : new[] { i, i + 1, i + 2 });
        }

        public bool Empty => _v.Count == 0;

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000)
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetUVs(0, _uv);
            m.SetColors(_c);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A MeshRenderer piece under <paramref name="parent"/> (no shadows; optional static collider).</summary>
        public static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
