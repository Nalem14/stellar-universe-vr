using Core.Vfx;
using UnityEngine;

namespace Core.Crew
{
    /// <summary>
    /// The guide's spotlight in the room: a soft ring of light on the deck round what the step is about (an
    /// officer, the holo table, the corridor door, the Guide button), a faint column rising from it and a
    /// chevron bobbing overhead, turned to the captain. One shared set of meshes and two materials; moved
    /// from target to target, never rebuilt.
    /// </summary>
    public sealed class TutorialBeacon : MonoBehaviour
    {
        Transform _ring;
        Transform _column;
        Transform _chevron;
        Transform _target;
        Vector3 _offset;
        float _radius = 0.5f;
        float _height = 2f;
        static Material _glow;
        static Material _solid;
        static Mesh _ringMesh;
        static Mesh _columnMesh;
        static Mesh _chevronMesh;

        public static TutorialBeacon Build(Transform room, Color accent)
        {
            var go = new GameObject("TutorialBeacon");
            go.transform.SetParent(room, false);
            var b = go.AddComponent<TutorialBeacon>();

            b._ring = Piece(go.transform, "Ring", RingMesh(), GlowMat(accent));
            b._column = new GameObject("Column").transform;
            b._column.SetParent(go.transform, false);
            for (var i = 0; i < 2; i++)
            {
                var blade = Piece(b._column, "Blade" + i, ColumnMesh(), GlowMat(accent));
                blade.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            }

            b._chevron = Piece(go.transform, "Chevron", ChevronMesh(), SolidMat(accent));
            b._chevron.localScale = Vector3.one * 1.6f;
            go.SetActive(false);
            return b;
        }

        static Transform Piece(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        /// <summary>Ring the deck under <paramref name="target"/> (radius in metres), chevron at <paramref name="height"/>.</summary>
        public void Point(Transform target, Vector3 offset, float radius, float height)
        {
            _target = target;
            _offset = offset;
            _radius = radius;
            _height = height;
            gameObject.SetActive(target != null);
            if (target != null)
                Place();
        }

        public void Clear()
        {
            _target = null;
            gameObject.SetActive(false);
        }

        void Place()
        {
            var room = transform.parent;
            var p = room.InverseTransformPoint(_target.position + _offset);
            // Rooted on the deck (the room floor is y = 0), whatever the target's own height.
            transform.localPosition = new Vector3(p.x, 0.012f, p.z);
            transform.localRotation = Quaternion.identity;
            _column.localScale = new Vector3(_radius * 0.9f, _height * 0.8f, _radius * 0.9f);
        }

        void LateUpdate()
        {
            if (_target == null)
            {
                Clear();
                return;
            }

            Place();
            var t = Time.unscaledTime;
            var pulse = 1f + 0.06f * Mathf.Sin(t * 3.2f);
            _ring.localScale = new Vector3(_radius * 2f * pulse, 1f, _radius * 2f * pulse);
            _chevron.localPosition = new Vector3(0f, _height + 0.06f * Mathf.Sin(t * 2.4f), 0f);
            // The chevron turns to face the viewer (yaw only), so it always reads as "here".
            var cam = Camera.main;
            if (cam != null)
            {
                var d = cam.transform.position - _chevron.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f)
                    _chevron.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up);
            }
        }

        // ── Art ─────────────────────────────────────────────────────────────────

        static Material GlowMat(Color c)
        {
            if (_glow != null)
                return _glow;
            var shader = Shader.Find("SU/ParticleGlow") ?? Shader.Find("SU/UnlitEmissive");
            _glow = new Material(shader) { name = "SU_TutorialGlow", mainTexture = CombatFxKit.BeamTexture() };
            if (_glow.HasProperty("_Color"))
                _glow.SetColor("_Color", c);
            if (_glow.HasProperty("_EmissionMul"))
                _glow.SetFloat("_EmissionMul", 1.6f);
            return _glow;
        }

        static Material SolidMat(Color c)
        {
            if (_solid != null)
                return _solid;
            _solid = new Material(Shader.Find("SU/UnlitEmissive")) { name = "SU_TutorialChevron" };
            if (_solid.HasProperty("_Color"))
                _solid.SetColor("_Color", c);
            if (_solid.HasProperty("_Emission"))
                _solid.SetColor("_Emission", c * 1.4f);
            return _solid;
        }

        /// <summary>Flat annulus on XZ, unit diameter; v runs across the band so the beam profile softens both edges.</summary>
        static Mesh RingMesh()
        {
            if (_ringMesh != null)
                return _ringMesh;
            const int seg = 64;
            const float inner = 0.4f;
            const float outer = 0.5f;
            var v = new Vector3[(seg + 1) * 2];
            var uv = new Vector2[v.Length];
            var tri = new int[seg * 6];
            for (var s = 0; s <= seg; s++)
            {
                var a = s / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[s * 2] = dir * inner;
                v[s * 2 + 1] = dir * outer;
                uv[s * 2] = new Vector2(s / (float)seg, 0f);
                uv[s * 2 + 1] = new Vector2(s / (float)seg, 1f);
                if (s == seg)
                    continue;
                var t = s * 6;
                var o = s * 2;
                tri[t] = o;
                tri[t + 1] = o + 1;
                tri[t + 2] = o + 2;
                tri[t + 3] = o + 1;
                tri[t + 4] = o + 3;
                tri[t + 5] = o + 2;
            }

            _ringMesh = new Mesh { name = "SU_TutorialRing", vertices = v, uv = uv, triangles = tri, colors = Fill(v.Length, 1f) };
            _ringMesh.RecalculateBounds();
            return _ringMesh;
        }

        /// <summary>Vertical blade, unit width and height, bright at the deck and gone at the top.</summary>
        static Mesh ColumnMesh()
        {
            if (_columnMesh != null)
                return _columnMesh;
            var v = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f) };
            var col = new[] { new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f) };
            _columnMesh = new Mesh { name = "SU_TutorialColumn", vertices = v, uv = uv, colors = col, triangles = new[] { 0, 2, 1, 2, 3, 1 } };
            _columnMesh.RecalculateBounds();
            return _columnMesh;
        }

        /// <summary>A downward chevron (two bevelled strokes meeting at the tip), 0.22 m wide, a little depth.</summary>
        static Mesh ChevronMesh()
        {
            if (_chevronMesh != null)
                return _chevronMesh;
            const float w = 0.11f;
            const float h = 0.12f;
            const float th = 0.035f;
            const float d = 0.012f;
            // Outline of the V (front face), clockwise from the left top.
            var outline = new[]
            {
                new Vector2(-w, h), new Vector2(-w + th, h), new Vector2(0f, th * 1.2f),
                new Vector2(w - th, h), new Vector2(w, h), new Vector2(0f, 0f)
            };
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            foreach (var z in new[] { -d, d })
                foreach (var p in outline)
                    verts.Add(new Vector3(p.x, p.y, z));
            // Front and back faces: two quads per face (left and right stroke).
            void Quad(int a, int b, int c, int e, bool flip)
            {
                if (flip)
                    tris.AddRange(new[] { a, c, b, a, e, c });
                else
                    tris.AddRange(new[] { a, b, c, a, c, e });
            }

            for (var f = 0; f < 2; f++)
            {
                var o = f * 6;
                Quad(o + 0, o + 1, o + 2, o + 5, f == 1);
                Quad(o + 2, o + 3, o + 4, o + 5, f == 1);
            }

            // Sides.
            for (var i = 0; i < 6; i++)
            {
                var j = (i + 1) % 6;
                tris.AddRange(new[] { i, j, 6 + j, i, 6 + j, 6 + i });
                tris.AddRange(new[] { i, 6 + j, j, i, 6 + i, 6 + j });
            }

            _chevronMesh = new Mesh { name = "SU_TutorialChevron" };
            _chevronMesh.SetVertices(verts);
            _chevronMesh.SetTriangles(tris, 0);
            _chevronMesh.RecalculateNormals();
            _chevronMesh.RecalculateBounds();
            return _chevronMesh;
        }

        static Color[] Fill(int n, float a)
        {
            var c = new Color[n];
            for (var i = 0; i < n; i++)
                c[i] = new Color(1f, 1f, 1f, a);
            return c;
        }
    }
}
