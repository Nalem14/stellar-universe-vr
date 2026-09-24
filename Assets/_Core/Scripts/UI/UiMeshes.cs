using System.Collections.Generic;
using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// Procedural rounded hardware for the bridge UI kit (button caps, bezels, screen chassis).
    /// One shared Mesh per (size, radius) — never a scaled Unity cube, so labels and bevels keep
    /// true proportions. Normals are the true rounded-box normals, so SU/ConsoleMetal lights bevels.
    /// </summary>
    public static class UiMeshes
    {
        const int Segments = 5;

        static readonly Dictionary<long, Mesh> Cache = new();

        /// <summary>Rounded box centred on the origin, size in metres.</summary>
        public static Mesh RoundedBox(Vector3 size, float radius)
        {
            size = new Vector3(Quantize(size.x), Quantize(size.y), Quantize(size.z));
            radius = Mathf.Min(Quantize(radius), Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f);
            var key = Key(size, radius);
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var mesh = Build(size, radius);
            mesh.name = $"UiRoundedBox_{size.x:F3}x{size.y:F3}x{size.z:F3}_r{radius:F3}";
            Cache[key] = mesh;
            return mesh;
        }

        static Mesh Build(Vector3 size, float radius)
        {
            var half = size * 0.5f;
            var inner = half - Vector3.one * radius;
            var verts = new List<Vector3>(6 * (Segments + 1) * (Segments + 1));
            var normals = new List<Vector3>(verts.Capacity);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(6 * Segments * Segments * 6);

            // Six subdivided faces of a unit cube, each vertex pushed onto the rounded surface.
            AddFace(Vector3.forward, Vector3.right, Vector3.up);
            AddFace(Vector3.back, Vector3.left, Vector3.up);
            AddFace(Vector3.right, Vector3.back, Vector3.up);
            AddFace(Vector3.left, Vector3.forward, Vector3.up);
            AddFace(Vector3.up, Vector3.right, Vector3.back);
            AddFace(Vector3.down, Vector3.right, Vector3.forward);

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;

            void AddFace(Vector3 normal, Vector3 axisU, Vector3 axisV)
            {
                var start = verts.Count;
                for (var y = 0; y <= Segments; y++)
                {
                    for (var x = 0; x <= Segments; x++)
                    {
                        var u = x / (float)Segments;
                        var v = y / (float)Segments;
                        // Cluster samples toward edges so the bevel gets most of the vertices.
                        var su = Edge(u) * 2f - 1f;
                        var sv = Edge(v) * 2f - 1f;
                        var p = normal + axisU * su + axisV * sv;
                        var onBox = Vector3.Scale(p, half);
                        var clamped = new Vector3(
                            Mathf.Clamp(onBox.x, -inner.x, inner.x),
                            Mathf.Clamp(onBox.y, -inner.y, inner.y),
                            Mathf.Clamp(onBox.z, -inner.z, inner.z));
                        var dir = onBox - clamped;
                        var n = dir.sqrMagnitude > 1e-10f ? dir.normalized : normal;
                        verts.Add(clamped + n * radius);
                        normals.Add(n);
                        uvs.Add(new Vector2(u, v));
                    }
                }

                var row = Segments + 1;
                for (var y = 0; y < Segments; y++)
                {
                    for (var x = 0; x < Segments; x++)
                    {
                        // Unity front face: Cross(b - a, c - a) points out. cross(axisU, axisV) == normal.
                        var i = start + y * row + x;
                        tris.Add(i);
                        tris.Add(i + 1);
                        tris.Add(i + row);
                        tris.Add(i + 1);
                        tris.Add(i + row + 1);
                        tris.Add(i + row);
                    }
                }
            }
        }

        static float Edge(float t)
        {
            // Smoothstep-shaped spacing: dense near 0 and 1 (bevels), sparse in the flat middle.
            return t * t * (3f - 2f * t) * 0.5f + t * 0.5f;
        }

        static float Quantize(float v) => Mathf.Max(0.001f, Mathf.Round(v * 1000f) / 1000f);

        static long Key(Vector3 s, float r)
        {
            unchecked
            {
                long k = (long)(s.x * 1000f);
                k = k * 4001 + (long)(s.y * 1000f);
                k = k * 4001 + (long)(s.z * 1000f);
                k = k * 4001 + (long)(r * 1000f);
                return k;
            }
        }
    }
}
