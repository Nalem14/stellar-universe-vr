using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Orbital defence platform art, built once and shared: a faceted armoured disc (8-sided lathe) with a
    /// domed twin-barrel turret on top and a sensor spire below (one hull mesh, SU/HullMetal on the ship
    /// armour texture), plus a rim light band and turret slit (one emissive mesh). Unit radius 1 — the
    /// caller scales by tier. Two draw calls per platform, no per-instance material.
    /// </summary>
    public static class DefensePlatformKit
    {
        static Mesh _hull;
        static Mesh _glow;
        static Material _hullMat;
        static Material _glowOurs;
        static Material _glowHostile;

        public static Mesh Hull => _hull != null ? _hull : _hull = BuildHull();
        public static Mesh Glow => _glow != null ? _glow : _glow = BuildGlow();

        public static Material HullMat()
        {
            if (_hullMat != null)
                return _hullMat;
            var shader = Shader.Find("SU/HullMetal") ?? Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            _hullMat = new Material(shader) { name = "SU_DefensePlatform", enableInstancing = true };
            var tex = Resources.Load<Texture2D>("Ships/Armor");
            if (tex != null && _hullMat.HasProperty("_MainTex"))
            {
                _hullMat.mainTexture = tex;
                _hullMat.SetTextureScale("_MainTex", Vector2.one * 1.6f);
            }

            if (_hullMat.HasProperty("_Color")) _hullMat.SetColor("_Color", new Color(0.68f, 0.72f, 0.78f, 1f));
            if (_hullMat.HasProperty("_RimColor")) _hullMat.SetColor("_RimColor", new Color(0.35f, 0.75f, 0.9f, 1f));
            if (_hullMat.HasProperty("_EmissionMul")) _hullMat.SetFloat("_EmissionMul", 0f);
            if (_hullMat.HasProperty("_PanelScale")) _hullMat.SetFloat("_PanelScale", 0.8f);
            if (_hullMat.HasProperty("_Groove")) _hullMat.SetFloat("_Groove", 0.08f);
            if (_hullMat.HasProperty("_Wear")) _hullMat.SetFloat("_Wear", 0.18f);
            return _hullMat;
        }

        /// <summary>Rim light: cyan on our worlds, red on a hostile one.</summary>
        public static Material GlowMat(bool ours)
        {
            ref var slot = ref ours ? ref _glowOurs : ref _glowHostile;
            if (slot != null)
                return slot;
            var shader = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            var c = ours ? new Color(0.35f, 0.95f, 1f, 1f) : new Color(1f, 0.35f, 0.25f, 1f);
            slot = new Material(shader) { name = ours ? "SU_DefenseGlowOurs" : "SU_DefenseGlowHostile", enableInstancing = true };
            if (slot.HasProperty("_Color")) slot.SetColor("_Color", c);
            if (slot.HasProperty("_Emission")) slot.SetColor("_Emission", c);
            if (slot.HasProperty("_EmissionMul")) slot.SetFloat("_EmissionMul", 3.6f);
            return slot;
        }

        /// <summary>A platform GameObject (hull + glow children) at unit radius.</summary>
        public static GameObject Create(Transform parent, string name, bool ours)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            Part(root.transform, "Hull", Hull, HullMat());
            Part(root.transform, "Glow", Glow, GlowMat(ours));
            return root;
        }

        static void Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ── Meshes ────────────────────────────────────────────────────────────────

        static Mesh BuildHull()
        {
            var b = new Builder();
            // Armoured disc: flat underside bevel, thick rim, stepped deck (faceted, 8 sides).
            b.Lathe(new[]
            {
                new Vector2(0f, -0.3f), new Vector2(0.42f, -0.24f), new Vector2(0.9f, -0.08f), new Vector2(1f, -0.02f),
                new Vector2(1f, 0.04f), new Vector2(0.86f, 0.1f), new Vector2(0.62f, 0.12f), new Vector2(0.56f, 0.17f),
                new Vector2(0.3f, 0.18f), new Vector2(0f, 0.18f)
            }, 8, true, Matrix4x4.identity);
            // Turret dome (smooth).
            b.Lathe(new[]
            {
                new Vector2(0f, 0.18f), new Vector2(0.3f, 0.18f), new Vector2(0.29f, 0.25f), new Vector2(0.22f, 0.33f),
                new Vector2(0.1f, 0.37f), new Vector2(0f, 0.38f)
            }, 14, false, Matrix4x4.identity);
            // Twin barrels along +Z from the dome.
            var barrel = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.045f, 0f), new Vector2(0.045f, 0.12f), new Vector2(0.032f, 0.14f),
                new Vector2(0.032f, 0.62f), new Vector2(0.042f, 0.64f), new Vector2(0.042f, 0.7f), new Vector2(0f, 0.7f)
            };
            foreach (var side in new[] { -0.09f, 0.09f })
                b.Lathe(barrel, 8, false,
                    Matrix4x4.TRS(new Vector3(side, 0.28f, 0.12f), Quaternion.Euler(90f, 0f, 0f), Vector3.one));
            // Sensor spire under the disc.
            b.Lathe(new[]
            {
                new Vector2(0f, -0.3f), new Vector2(0.1f, -0.3f), new Vector2(0.05f, -0.48f), new Vector2(0.018f, -0.78f),
                new Vector2(0f, -0.8f)
            }, 6, true, Matrix4x4.identity);
            // Three radiator vanes on the rim.
            for (var i = 0; i < 3; i++)
            {
                var rot = Quaternion.Euler(0f, 60f + i * 120f, 0f);
                b.Box(rot * new Vector3(0f, 0.01f, 1.18f), rot, new Vector3(0.34f, 0.025f, 0.4f));
            }

            return b.ToMesh("SU_DefensePlatformHull");
        }

        static Mesh BuildGlow()
        {
            var b = new Builder();
            // Rim band, just proud of the armour.
            b.Lathe(new[] { new Vector2(1.014f, -0.03f), new Vector2(1.014f, 0.05f) }, 24, false, Matrix4x4.identity);
            // Turret sight slit.
            b.Lathe(new[] { new Vector2(0.298f, 0.2f), new Vector2(0.293f, 0.245f) }, 14, false, Matrix4x4.identity);
            // Deck ring lights.
            b.Lathe(new[] { new Vector2(0.58f, 0.126f), new Vector2(0.68f, 0.12f) }, 24, false, Matrix4x4.identity);
            return b.ToMesh("SU_DefensePlatformGlow");
        }

        sealed class Builder
        {
            readonly List<Vector3> _v = new();
            readonly List<Vector3> _n = new();
            readonly List<Vector2> _uv = new();
            readonly List<int> _t = new();

            /// <summary>Revolve a (radius, height) profile around Y. Flat = faceted normals per quad.</summary>
            public void Lathe(Vector2[] profile, int segments, bool flat, Matrix4x4 m)
            {
                for (var s = 0; s < segments; s++)
                {
                    var a0 = s / (float)segments * Mathf.PI * 2f;
                    var a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                    for (var i = 0; i < profile.Length - 1; i++)
                    {
                        var p0 = profile[i];
                        var p1 = profile[i + 1];
                        var v00 = Ring(p0, a0);
                        var v01 = Ring(p0, a1);
                        var v10 = Ring(p1, a0);
                        var v11 = Ring(p1, a1);
                        Vector3 n00, n01, n10, n11;
                        if (flat)
                        {
                            var fn = Vector3.Cross(v10 - v00, v01 - v00).normalized;
                            if (fn.sqrMagnitude < 0.5f)
                                fn = Vector3.Cross(v11 - v10, v00 - v10).normalized;
                            n00 = n01 = n10 = n11 = fn;
                        }
                        else
                        {
                            var edge = p1 - p0;
                            var pn = new Vector2(edge.y, -edge.x).normalized;
                            n00 = n10 = Radial(pn, a0);
                            n01 = n11 = Radial(pn, a1);
                        }

                        var u0 = s / (float)segments;
                        var u1 = (s + 1) / (float)segments;
                        Quad(m, v00, v10, v11, v01, n00, n10, n11, n01,
                            new Vector2(u0, p0.y), new Vector2(u0, p1.y), new Vector2(u1, p1.y), new Vector2(u1, p0.y));
                    }
                }
            }

            public void Box(Vector3 center, Quaternion rot, Vector3 size)
            {
                var m = Matrix4x4.TRS(center, rot, size);
                var faces = new[]
                {
                    (Vector3.up, Vector3.forward, Vector3.right), (Vector3.down, Vector3.back, Vector3.right),
                    (Vector3.right, Vector3.up, Vector3.forward), (Vector3.left, Vector3.up, Vector3.back),
                    (Vector3.forward, Vector3.up, Vector3.left), (Vector3.back, Vector3.up, Vector3.right)
                };
                foreach (var (n, u, r) in faces)
                {
                    var c = n * 0.5f;
                    var a = c - u * 0.5f - r * 0.5f;
                    var bb = c + u * 0.5f - r * 0.5f;
                    var cc = c + u * 0.5f + r * 0.5f;
                    var d = c - u * 0.5f + r * 0.5f;
                    Quad(m, a, bb, cc, d, n, n, n, n, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                }
            }

            static Vector3 Ring(Vector2 p, float a) => new(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x);
            static Vector3 Radial(Vector2 n, float a) => new Vector3(Mathf.Cos(a) * n.x, n.y, Mathf.Sin(a) * n.x).normalized;

            void Quad(Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc,
                Vector3 nd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                var i = _v.Count;
                _v.Add(m.MultiplyPoint3x4(a));
                _v.Add(m.MultiplyPoint3x4(b));
                _v.Add(m.MultiplyPoint3x4(c));
                _v.Add(m.MultiplyPoint3x4(d));
                _n.Add(m.MultiplyVector(na).normalized);
                _n.Add(m.MultiplyVector(nb).normalized);
                _n.Add(m.MultiplyVector(nc).normalized);
                _n.Add(m.MultiplyVector(nd).normalized);
                _uv.Add(ua);
                _uv.Add(ub);
                _uv.Add(uc);
                _uv.Add(ud);
                // Two-sided winding check: keep the face pointing along its normal.
                var faceN = Vector3.Cross(_v[i + 1] - _v[i], _v[i + 2] - _v[i]);
                if (Vector3.Dot(faceN, _n[i] + _n[i + 2]) >= 0f)
                {
                    _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
                    _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
                }
                else
                {
                    _t.Add(i); _t.Add(i + 2); _t.Add(i + 1);
                    _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
