using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The arrival airlock's shell, all curves: a round hall whose wall rises from a floor cove, curls into a
    /// cornice cove and closes in a dome around an open oculus — one lathe per band, not six boxes. A panoramic
    /// window takes the front 120° between sill and lintel (open, like the bridge hublots) onto the shared star
    /// sky and a planet below; ribs follow the whole profile from floor to oculus and turn into mullions across
    /// the window. Light coves in the cornice and around the floor. A few meshes on shared materials.
    /// Hall local: centre <see cref="Centre"/>, front +z; the terminal stands just in front of the centre.
    /// </summary>
    public static class SasShell
    {
        public static readonly Vector3 Centre = new(0f, 0f, 0.9f);
        public const float Radius = 4.3f;
        const float WindowHalf = 60f;
        const float SillY = 0.95f;
        const float LintelY = 2.9f;
        const float WallTop = 2.95f;
        const float OculusY = 4.25f;
        const float OculusR = 0.75f;
        const int Segments = 72;

        /// <summary>Angle (° from +z, clockwise seen from above) of the door to the bridge.</summary>
        public const float DoorAngle = 90f;

        public static void Build(Transform room, CicArtKit art)
        {
            var wall = art.SoftPanel(0.12f);
            var dark = art.DarkPanel(0.1f);
            var metal = art.MetalPanel(0.2f);
            var deck = art.DeckMat(0.35f);
            var cyan = art.CyanEmit(2.6f);
            var amber = art.Lit(Texture2D.whiteTexture, CicArtKit.Amber, 1.8f);

            var root = new GameObject("SasShell").transform;
            root.SetParent(room, false);
            root.localPosition = Centre;

            // Dome and cornice, all the way round.
            var upper = new List<Vector2>();
            for (var i = 0; i <= 10; i++)
            {
                var t = i / 10f * Mathf.PI * 0.5f;
                upper.Add(new Vector2(OculusR + (Radius - 0.32f - OculusR) * Mathf.Sin(t), 3.22f + (OculusY - 3.22f) * Mathf.Cos(t)));
            }

            upper.Add(new Vector2(Radius - 0.1f, 3.12f));
            upper.Add(new Vector2(Radius, WallTop));
            upper.Add(new Vector2(Radius, LintelY));
            Part(root, "Dome", Lathe(upper, -180f, 180f, Segments, "SU_SasDome"), wall, collider: false);

            // The wall band between lintel and sill, except across the window.
            var mid = new List<Vector2> { new(Radius, LintelY), new(Radius, SillY) };
            Part(root, "Wall", Lathe(mid, WindowHalf, 360f - WindowHalf, Segments, "SU_SasWall"), wall, collider: true);

            // Low wall and floor cove, all the way round.
            var low = new List<Vector2>
            {
                new(Radius, SillY), new(Radius, 0.34f), new(Radius - 0.06f, 0.16f), new(Radius - 0.16f, 0.05f),
                new(Radius - 0.3f, 0f)
            };
            Part(root, "Plinth", Lathe(low, -180f, 180f, Segments, "SU_SasPlinth"), dark, collider: true);
            Part(root, "Floor", Disc(Radius - 0.29f, Segments, "SU_SasFloor"), deck, collider: true);

            // Window sill and lintel: rounded ledges proud of the wall across the opening.
            var sill = new List<Vector2>
            {
                new(Radius + 0.05f, SillY + 0.02f), new(Radius - 0.22f, SillY + 0.02f), new(Radius - 0.3f, SillY - 0.02f),
                new(Radius - 0.27f, SillY - 0.08f), new(Radius, SillY - 0.1f)
            };
            Part(root, "Sill", Lathe(sill, -WindowHalf, WindowHalf, 24, "SU_SasSill"), metal, collider: true);
            var lintel = new List<Vector2>
            {
                new(Radius, LintelY + 0.08f), new(Radius - 0.18f, LintelY + 0.05f), new(Radius - 0.2f, LintelY - 0.02f),
                new(Radius + 0.05f, LintelY - 0.02f)
            };
            Part(root, "Lintel", Lathe(lintel, -WindowHalf, WindowHalf, 24, "SU_SasLintel"), metal, collider: false);
            Part(root, "SillGlow", Band(Radius - 0.305f, SillY - 0.035f, SillY - 0.01f, -WindowHalf, WindowHalf, 24, "SU_SasSillGlow"),
                cyan, collider: false);

            // Light coves: under the cornice (amber) and a floor ring (cyan); the oculus glows at the apex.
            Part(root, "CorniceCove", Band(Radius - 0.12f, 3.02f, 3.07f, -180f, 180f, Segments, "SU_SasCove"), amber, collider: false);
            Part(root, "FloorRing", Ring(Radius - 0.62f, Radius - 0.57f, 0.006f, Segments, "SU_SasFloorRing"), cyan, collider: false);
            // The oculus stays open onto the star sky; a thin lit rim and a lip around it.
            Part(root, "OculusRim", Ring(OculusR - 0.06f, OculusR + 0.04f, OculusY - 0.01f, 36, "SU_SasOculusRim", down: true),
                amber, collider: false);

            // Ribs along the whole profile every 30°, jambs at the window edges; the door keeps a clear bay.
            var ribProfile = new List<Vector2>(upper);
            ribProfile.AddRange(new[] { new Vector2(Radius, SillY), new Vector2(Radius, 0.34f), new Vector2(Radius - 0.06f, 0.16f) });
            var ribMesh = Rib(ribProfile, 0.07f, 0.09f, "SU_SasRib");
            var mullion = Rib(new List<Vector2> { new(Radius, LintelY), new(Radius, SillY) }, 0.05f, 0.1f, "SU_SasMullion");
            var slit = Rib(new List<Vector2> { new(Radius - 0.1f, 2.6f), new(Radius - 0.1f, 1.2f) }, 0.012f, 0.004f, "SU_SasSlit");
            for (var a = 15f; a < 360f; a += 30f)
            {
                var inWindow = a < WindowHalf || a > 360f - WindowHalf;
                var nearDoor = Mathf.Abs(Mathf.DeltaAngle(a, DoorAngle)) < 20f;
                if (nearDoor)
                    continue;
                var rot = Quaternion.Euler(0f, a, 0f);
                if (inWindow)
                {
                    Part(root, "Mullion", mullion, metal, collider: false).transform.localRotation = rot;
                    continue;
                }

                Part(root, "Rib", ribMesh, metal, collider: false).transform.localRotation = rot;
                Part(root, "RibSlit", slit, cyan, collider: false).transform.localRotation = rot;
            }

            foreach (var a in new[] { -WindowHalf, WindowHalf, DoorAngle - 12f, DoorAngle + 12f })
                Part(root, "Jamb", ribMesh, metal, collider: false).transform.localRotation = Quaternion.Euler(0f, a, 0f);

            BuildView(room, art);
        }

        /// <summary>Beyond the window: the shared star sky and a lit planet far below the horizon line.</summary>
        static void BuildView(Transform room, CicArtKit art)
        {
            SpaceBackdrop.Ensure(room);
            var planet = new GameObject("SasPlanet");
            planet.transform.SetParent(room, false);
            planet.transform.localPosition = new Vector3(-140f, -150f, 520f);
            planet.transform.localScale = Vector3.one * 440f;
            planet.AddComponent<MeshFilter>().sharedMesh = SphereMesh.Smooth;
            var mr = planet.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SystemBodyKit.PlanetMat(SystemBodyKit.PlanetKind.Terra, SystemBodyKit.Ownership.Owned);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var atmo = new GameObject("Atmosphere");
            atmo.transform.SetParent(planet.transform, false);
            atmo.transform.localScale = Vector3.one * 1.035f;
            atmo.AddComponent<MeshFilter>().sharedMesh = SphereMesh.Smooth;
            var ar = atmo.AddComponent<MeshRenderer>();
            ar.sharedMaterial = SystemBodyKit.AtmosphereMat(SystemBodyKit.PlanetKind.Terra, SystemBodyKit.Ownership.Owned);
            ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Sunlight from up and to the right: a crescent terminator across the planet.
            SystemBodyKit.ApplyStarLightDirection(new Vector3(-0.7f, -0.35f, 0.62f).normalized);
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return r;
        }

        // ── Meshes ────────────────────────────────────────────────────────────────

        static Vector3 At(Vector2 p, float deg)
        {
            var a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * p.x, p.y, Mathf.Cos(a) * p.x);
        }

        /// <summary>
        /// Revolve a (radius, height) profile from <paramref name="a0"/> to <paramref name="a1"/> degrees, faces toward
        /// the axis (profile listed from the top down). UV in metres (arc length, profile length) for tiled panels.
        /// </summary>
        static Mesh Lathe(List<Vector2> profile, float a0, float a1, int segments, string name)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            var cols = profile.Count;
            var along = new float[cols];
            for (var i = 1; i < cols; i++)
                along[i] = along[i - 1] + Vector2.Distance(profile[i - 1], profile[i]);
            for (var s = 0; s <= segments; s++)
            {
                var deg = Mathf.Lerp(a0, a1, s / (float)segments);
                for (var i = 0; i < cols; i++)
                {
                    var p = profile[i];
                    v.Add(At(p, deg));
                    // Profile normal: the edge turned toward the axis (smooth, averaged at joints).
                    var e0 = i > 0 ? profile[i] - profile[i - 1] : profile[1] - profile[0];
                    var e1 = i < cols - 1 ? profile[i + 1] - profile[i] : e0;
                    var e = (e0.normalized + e1.normalized).normalized;
                    var pn = new Vector2(e.y, -e.x);
                    var a = deg * Mathf.Deg2Rad;
                    n.Add(new Vector3(Mathf.Sin(a) * pn.x, pn.y, Mathf.Cos(a) * pn.x).normalized);
                    uv.Add(new Vector2(deg * Mathf.Deg2Rad * Radius * 0.5f, along[i] * 0.5f));
                }
            }

            for (var s = 0; s < segments; s++)
            for (var i = 0; i < cols - 1; i++)
            {
                var a = s * cols + i;
                var b = a + cols;
                Tri(v, n, t, a, a + 1, b + 1);
                Tri(v, n, t, a, b + 1, b);
            }

            return Finish(name, v, n, uv, t);
        }

        /// <summary>Keep each triangle facing its normals (the lathe may run either way round).</summary>
        static void Tri(List<Vector3> v, List<Vector3> n, List<int> t, int a, int b, int c)
        {
            var face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(face, n[a] + n[b] + n[c]) >= 0f)
                t.AddRange(new[] { a, b, c });
            else
                t.AddRange(new[] { a, c, b });
        }

        static Mesh Disc(float r, int segments, string name, float y = 0f, bool down = false)
        {
            var v = new List<Vector3> { new(0f, y, 0f) };
            var n = new List<Vector3> { down ? Vector3.down : Vector3.up };
            var uv = new List<Vector2> { Vector2.zero };
            var t = new List<int>();
            for (var s = 0; s < segments; s++)
            {
                var a = s / (float)segments * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
                v.Add(p);
                n.Add(down ? Vector3.down : Vector3.up);
                uv.Add(new Vector2(p.x, p.z) * 0.5f);
            }

            for (var s = 0; s < segments; s++)
                Tri(v, n, t, 0, 1 + s, 1 + (s + 1) % segments);
            return Finish(name, v, n, uv, t);
        }

        static Mesh Ring(float r0, float r1, float y, int segments, string name, bool down = false)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (var s = 0; s <= segments; s++)
            {
                var a = s / (float)segments * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(d * r0 + Vector3.up * y);
                v.Add(d * r1 + Vector3.up * y);
                n.Add(down ? Vector3.down : Vector3.up);
                n.Add(down ? Vector3.down : Vector3.up);
                uv.Add(new Vector2(s / (float)segments, 0f));
                uv.Add(new Vector2(s / (float)segments, 1f));
            }

            for (var s = 0; s < segments; s++)
            {
                var a = s * 2;
                Tri(v, n, t, a, a + 1, a + 3);
                Tri(v, n, t, a, a + 3, a + 2);
            }

            return Finish(name, v, n, uv, t);
        }

        /// <summary>A vertical light strip on the cylinder r, facing the axis.</summary>
        static Mesh Band(float r, float y0, float y1, float a0, float a1, int segments, string name) =>
            Lathe(new List<Vector2> { new(r, y1), new(r, y0) }, a0, a1, segments, name);

        /// <summary>
        /// A rib swept along a (radius, height) profile at angle 0: front face inset by <paramref name="depth"/>
        /// toward the axis, two side faces back to the wall. Rotated about Y by its user.
        /// </summary>
        static Mesh Rib(List<Vector2> profile, float halfWidth, float depth, string name)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            var count = profile.Count;
            var inset = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var e0 = i > 0 ? profile[i] - profile[i - 1] : profile[1] - profile[0];
                var e1 = i < count - 1 ? profile[i + 1] - profile[i] : e0;
                var e = (e0.normalized + e1.normalized).normalized;
                inset[i] = profile[i] + new Vector2(e.y, -e.x) * depth;
            }

            void Strip(System.Func<int, Vector3> a, System.Func<int, Vector3> b, System.Func<int, Vector3> hint)
            {
                var start = v.Count;
                for (var i = 0; i < count; i++)
                {
                    var pa = a(i);
                    var pb = b(i);
                    v.Add(pa);
                    v.Add(pb);
                    uv.Add(new Vector2(0f, i * 0.25f));
                    uv.Add(new Vector2(1f, i * 0.25f));
                    // Flat side / front normal: across the strip × along the profile, turned to the hint.
                    var j = Mathf.Min(i, count - 2);
                    var along = a(j + 1) - a(j);
                    var fn = Vector3.Cross(pb - pa, along).normalized;
                    if (Vector3.Dot(fn, hint(i)) < 0f)
                        fn = -fn;
                    n.Add(fn);
                    n.Add(fn);
                }

                for (var i = 0; i < count - 1; i++)
                {
                    var q = start + i * 2;
                    Tri(v, n, t, q, q + 1, q + 3);
                    Tri(v, n, t, q, q + 3, q + 2);
                }
            }

            Vector3 P(Vector2 p, float x) => new(x, p.y, p.x);
            Vector3 Toward(int i) => new Vector3(0f, inset[i].y - profile[i].y, inset[i].x - profile[i].x);
            Strip(i => P(inset[i], halfWidth), i => P(inset[i], -halfWidth), Toward);
            Strip(i => P(profile[i], halfWidth), i => P(inset[i], halfWidth), _ => Vector3.right);
            Strip(i => P(inset[i], -halfWidth), i => P(profile[i], -halfWidth), _ => Vector3.left);
            var mesh = Finish(name, v, n, uv, t);
            return mesh;
        }

        static Mesh Finish(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000)
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
