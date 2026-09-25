using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Shared smooth UV sphere (unit diameter, like the primitive) for bodies seen up close through the
    /// hublots: the 24-segment primitive shows facets on the limb when a planet fills the window from an
    /// orbital station. One mesh for every globe / atmosphere (~4 k tris), built once.
    /// </summary>
    public static class SphereMesh
    {
        const int Longitude = 56;
        const int Latitude = 36;
        static Mesh _mesh;

        public static Mesh Smooth
        {
            get
            {
                if (_mesh != null)
                    return _mesh;
                var verts = new Vector3[(Longitude + 1) * (Latitude + 1)];
                var normals = new Vector3[verts.Length];
                var uvs = new Vector2[verts.Length];
                var i = 0;
                for (var lat = 0; lat <= Latitude; lat++)
                {
                    var v = lat / (float)Latitude;
                    var theta = v * Mathf.PI;
                    for (var lon = 0; lon <= Longitude; lon++)
                    {
                        var u = lon / (float)Longitude;
                        var phi = u * Mathf.PI * 2f;
                        var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta),
                            Mathf.Sin(theta) * Mathf.Sin(phi));
                        normals[i] = n;
                        verts[i] = n * 0.5f;
                        uvs[i] = new Vector2(1f - u, 1f - v);
                        i++;
                    }
                }

                var tris = new int[Longitude * Latitude * 6];
                var t = 0;
                for (var lat = 0; lat < Latitude; lat++)
                {
                    for (var lon = 0; lon < Longitude; lon++)
                    {
                        var a = lat * (Longitude + 1) + lon;
                        var b = a + Longitude + 1;
                        tris[t++] = a;
                        tris[t++] = a + 1;
                        tris[t++] = b;
                        tris[t++] = b;
                        tris[t++] = a + 1;
                        tris[t++] = b + 1;
                    }
                }

                _mesh = new Mesh { name = "SmoothSphere", vertices = verts, normals = normals, uv = uvs, triangles = tris };
                _mesh.RecalculateTangents();
                _mesh.RecalculateBounds();
                return _mesh;
            }
        }
    }
}
