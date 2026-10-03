using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Set dressing merged at build time: boxes, cylinders and any mesh are collected per material and baked into
    /// one renderer per material (Quest draw calls: a room of trusses, pipes and cabinets costs a handful, not
    /// hundreds). Positions in the space of the transform passed to <see cref="Build"/>. No colliders.
    /// </summary>
    public sealed class MeshBatch
    {
        static Mesh _cube;
        static Mesh _cylinder;
        static Mesh _sphere;

        readonly Dictionary<Material, List<CombineInstance>> _parts = new();
        readonly List<Material> _order = new();

        /// <summary>A copy of the built-in primitive with white vertex colour (SU/HullInterior reads occlusion from it).</summary>
        static Mesh Primitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            Object.Destroy(go);
            var white = new Color[mesh.vertexCount];
            for (var i = 0; i < white.Length; i++)
                white[i] = Color.white;
            mesh.colors = white;
            mesh.name = "SU_Batch" + type;
            return mesh;
        }

        public static Mesh Cube => _cube != null ? _cube : _cube = Primitive(PrimitiveType.Cube);

        /// <summary>Unity cylinder: radius 0.5, height 2 along Y at scale 1.</summary>
        public static Mesh Cylinder => _cylinder != null ? _cylinder : _cylinder = Primitive(PrimitiveType.Cylinder);

        /// <summary>Unity sphere: radius 0.5 at scale 1.</summary>
        public static Mesh Sphere => _sphere != null ? _sphere : _sphere = Primitive(PrimitiveType.Sphere);

        public void Box(Vector3 pos, Vector3 size, Material mat, Quaternion? rot = null) =>
            Add(Cube, Matrix4x4.TRS(pos, rot ?? Quaternion.identity, size), mat);

        /// <summary>A cylinder of <paramref name="radius"/> and <paramref name="length"/> along <paramref name="axis"/>, centred at <paramref name="pos"/>.</summary>
        public void Tube(Vector3 pos, Vector3 axis, float radius, float length, Material mat)
        {
            var rot = Quaternion.FromToRotation(Vector3.up, axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up);
            Add(Cylinder, Matrix4x4.TRS(pos, rot, new Vector3(radius * 2f, length * 0.5f, radius * 2f)), mat);
        }

        /// <summary>A tube from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public void Pipe(Vector3 a, Vector3 b, float radius, Material mat) => Tube((a + b) * 0.5f, b - a, radius, Vector3.Distance(a, b), mat);

        /// <summary>A bar of square section from <paramref name="a"/> to <paramref name="b"/> (struts, webbing).</summary>
        public void Strut(Vector3 a, Vector3 b, float thickness, Material mat)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f)
                return;
            Add(Cube, Matrix4x4.TRS((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized),
                new Vector3(thickness, d.magnitude, thickness)), mat);
        }

        /// <summary>A sagging cable between two points (catenary-ish, <paramref name="segments"/> straight runs).</summary>
        public void Cable(Vector3 a, Vector3 b, float sag, float radius, Material mat, int segments = 6)
        {
            var prev = a;
            for (var i = 1; i <= segments; i++)
            {
                var u = i / (float)segments;
                var p = Vector3.Lerp(a, b, u) + Vector3.down * (sag * 4f * u * (1f - u));
                Pipe(prev, p, radius, mat);
                prev = p;
            }
        }

        public void Add(Mesh mesh, Matrix4x4 matrix, Material mat)
        {
            if (mesh == null || mat == null)
                return;
            if (!_parts.TryGetValue(mat, out var list))
            {
                list = new List<CombineInstance>();
                _parts[mat] = list;
                _order.Add(mat);
            }

            list.Add(new CombineInstance { mesh = mesh, transform = matrix });
        }

        /// <summary>Bake under <paramref name="parent"/> as <paramref name="name"/>: one child renderer per material.</summary>
        public Transform Build(Transform parent, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var i = 0;
            foreach (var mat in _order)
            {
                var list = _parts[mat];
                var mesh = new Mesh { name = name + "_" + i };
                var verts = 0;
                foreach (var c in list)
                    verts += c.mesh.vertexCount;
                if (verts > 65000)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(list.ToArray(), true, true);
                mesh.RecalculateBounds();
                LatheMesh.Part(root, name + "_" + mat.name, mesh, mat);
                i++;
            }

            _parts.Clear();
            _order.Clear();
            return root;
        }
    }
}
