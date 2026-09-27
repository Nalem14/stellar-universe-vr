using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Shared materials + procedural asteroid meshes for system-scale exterior.
    /// One palette per star type / planet kind / ownership — never new Material per body.
    /// </summary>
    public static class SystemBodyKit
    {
        public enum PlanetKind
        {
            Terra,
            Desert,
            Ice,
            Gas,
            Rock
        }

        public enum Ownership
        {
            Virgin,
            Owned,
            Foe
        }

        static readonly Color Cyan = new(0.25f, 0.92f, 1f, 1f);
        static readonly Color Amber = new(1f, 0.62f, 0.22f, 1f);
        static readonly Color ColdRim = new(0.45f, 0.62f, 0.85f, 1f);

        static readonly Dictionary<string, StarKit> Stars = new();
        static readonly Dictionary<int, Material> Planets = new();
        static readonly Dictionary<int, Material> Atmos = new();
        static Material _asteroid;
        static Material _nebulaBase;
        static readonly Dictionary<int, Material> Nebulae = new();
        static readonly Mesh[] AsteroidMeshes = new Mesh[6];
        static bool _meshesReady;
        static Texture2D _starTex;
        static Texture2D _coronaTex;
        static Texture2D _asteroidTex;
        static Texture2D _nebulaTex;
        static Texture2D _softGlow;
        static readonly Dictionary<PlanetKind, Texture2D> PlanetTex = new();

        public static PlanetKind ClassifyPlanet(int slot, int id, int habitability = 0)
        {
            if (habitability >= 6)
                return PlanetKind.Terra;
            if (habitability >= 4)
                return (Stable(id) % 2) == 0 ? PlanetKind.Terra : PlanetKind.Desert;
            if (habitability == 3)
                return PlanetKind.Desert;
            if (habitability == 2)
                return PlanetKind.Ice;
            if (habitability == 1)
                return PlanetKind.Rock;

            var s = Mathf.Max(1, slot);
            var h = Stable(id);
            if (s <= 1)
                return (h % 3) == 0 ? PlanetKind.Rock : PlanetKind.Desert;
            if (s == 2)
                return (h % 2) == 0 ? PlanetKind.Terra : PlanetKind.Desert;
            if (s == 3 || s == 4)
                return (h % 5) == 0 ? PlanetKind.Gas : PlanetKind.Terra;
            if (s >= 8)
                return (h % 3) == 0 ? PlanetKind.Rock : PlanetKind.Ice;
            return (h % 4) switch
            {
                0 => PlanetKind.Gas,
                1 => PlanetKind.Ice,
                2 => PlanetKind.Rock,
                _ => PlanetKind.Terra
            };
        }

        public static Ownership ResolveOwnership(int planetUserId, int ownedUserId)
        {
            if (planetUserId <= 0)
                return Ownership.Virgin;
            if (ownedUserId > 0 && planetUserId == ownedUserId)
                return Ownership.Owned;
            return Ownership.Foe;
        }

        public static StarKit Star(string typeKey, int typeHash)
        {
            EnsureTextures();
            var key = NormalizeStarKey(typeKey, typeHash);
            if (Stars.TryGetValue(key, out var kit))
                return kit;
            kit = BuildStar(key);
            Stars[key] = kit;
            return kit;
        }

        public static Material PlanetMat(PlanetKind kind, Ownership own)
        {
            EnsureTextures();
            var id = ((int)kind << 4) | (int)own;
            if (Planets.TryGetValue(id, out var mat))
                return mat;
            mat = MakePlanet(kind, own);
            Planets[id] = mat;
            return mat;
        }

        public static Material AtmosphereMat(PlanetKind kind, Ownership own)
        {
            EnsureTextures();
            var id = ((int)kind << 4) | (int)own | 0x100;
            if (Atmos.TryGetValue(id, out var mat))
                return mat;
            mat = MakeAtmosphere(kind, own);
            Atmos[id] = mat;
            return mat;
        }

        public static Material AsteroidMat()
        {
            EnsureTextures();
            if (_asteroid != null)
                return _asteroid;
            var shader = Shader.Find("SU/AsteroidRock") ?? Shader.Find("SU/HullMetal") ?? Shader.Find("Unlit/Color");
            _asteroid = new Material(shader);
            if (_asteroidTex != null && _asteroid.HasProperty("_MainTex"))
                _asteroid.mainTexture = _asteroidTex;
            if (_asteroid.HasProperty("_Color"))
                _asteroid.SetColor("_Color", new Color(0.55f, 0.48f, 0.4f));
            if (_asteroid.HasProperty("_RimColor"))
                _asteroid.SetColor("_RimColor", new Color(0.7f, 0.6f, 0.48f));
            return _asteroid;
        }

        static Mesh _ringMesh;
        static Material _ringMat;
        static Material _ringMatOwned;
        static Texture2D _ringTex;

        /// <summary>Planetary ring: a flat annulus (inner 0.62, outer 1 — scale by the outer radius), both faces.</summary>
        public static Mesh RingMesh()
        {
            if (_ringMesh != null)
                return _ringMesh;
            const int segs = 128;
            const float r0 = 0.62f;
            var v = new Vector3[(segs + 1) * 2];
            var uv = new Vector2[v.Length];
            var tris = new int[segs * 6];
            for (var i = 0; i <= segs; i++)
            {
                var a = i / (float)segs * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v[i * 2] = dir * r0;
                v[i * 2 + 1] = dir;
                uv[i * 2] = new Vector2(i / (float)segs * 8f, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)segs * 8f, 1f);
            }

            for (var i = 0; i < segs; i++)
            {
                var b = i * 2;
                tris[i * 6] = b;
                tris[i * 6 + 1] = b + 1;
                tris[i * 6 + 2] = b + 2;
                tris[i * 6 + 3] = b + 2;
                tris[i * 6 + 4] = b + 1;
                tris[i * 6 + 5] = b + 3;
            }

            _ringMesh = new Mesh { name = "SU_PlanetRing", vertices = v, uv = uv, triangles = tris };
            _ringMesh.RecalculateNormals();
            _ringMesh.RecalculateBounds();
            return _ringMesh;
        }

        /// <summary>
        /// Shared ring material: additive (the ring catches the star), banded across its width — ringlets, a dark
        /// Cassini-like gap, soft inner and outer edges. Owned worlds' rings run a little cooler.
        /// </summary>
        public static Material RingMat(bool owned)
        {
            if (owned ? _ringMatOwned != null : _ringMat != null)
                return owned ? _ringMatOwned : _ringMat;
            if (_ringTex == null)
            {
                const int w = 4, h = 256;
                _ringTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SU_RingBands", wrapMode = TextureWrapMode.Clamp };
                var rng = new System.Random(7);
                var px = new Color[w * h];
                for (var y = 0; y < h; y++)
                {
                    var r = y / (h - 1f);
                    var bands = 0.55f + 0.25f * Mathf.Sin(r * 71f) + 0.15f * Mathf.Sin(r * 173f + 1.3f) + (float)rng.NextDouble() * 0.12f;
                    var gap = 1f - Mathf.Exp(-Mathf.Pow((r - 0.58f) / 0.035f, 2f)) * 0.92f;
                    var edge = Mathf.SmoothStep(0f, 1f, r / 0.12f) * Mathf.SmoothStep(0f, 1f, (1f - r) / 0.18f);
                    var a = Mathf.Clamp01(bands * gap * edge);
                    var tint = Color.Lerp(new Color(0.95f, 0.86f, 0.7f), new Color(0.78f, 0.84f, 0.92f), r);
                    for (var x = 0; x < w; x++)
                        px[y * w + x] = new Color(tint.r * a, tint.g * a, tint.b * a, a);
                }

                _ringTex.SetPixels(px);
                _ringTex.Apply(false, true);
            }

            var shader = Shader.Find("SU/ParticleGlow") ?? Shader.Find("SU/UnlitEmissive");
            var m = new Material(shader) { name = owned ? "SU_PlanetRingOwned" : "SU_PlanetRing", mainTexture = _ringTex };
            var c = owned ? new Color(0.62f, 0.86f, 1f, 1f) : new Color(1f, 0.9f, 0.76f, 1f);
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", c);
            if (m.HasProperty("_Emission"))
                m.SetColor("_Emission", c);
            if (m.HasProperty("_EmissionMul"))
                m.SetFloat("_EmissionMul", 0.55f);
            if (owned)
                _ringMatOwned = m;
            else
                _ringMat = m;
            return m;
        }

        public static Material NebulaMat(int systemId)
        {
            EnsureTextures();
            var hueBucket = Mathf.Abs(Stable(systemId)) % 5;
            if (Nebulae.TryGetValue(hueBucket, out var mat))
                return mat;
            if (_nebulaBase == null)
            {
                var shader = Shader.Find("SU/NebulaCloud") ?? Shader.Find("SU/ParticleGlow") ?? Shader.Find("Sprites/Default");
                _nebulaBase = new Material(shader);
                if (_nebulaTex != null && _nebulaBase.HasProperty("_MainTex"))
                    _nebulaBase.mainTexture = _nebulaTex;
            }

            mat = new Material(_nebulaBase);
            var tint = hueBucket switch
            {
                0 => new Color(0.35f, 0.55f, 1f),
                1 => new Color(0.7f, 0.35f, 0.95f),
                2 => new Color(0.25f, 0.85f, 0.75f),
                3 => new Color(0.95f, 0.45f, 0.55f),
                _ => new Color(0.45f, 0.65f, 0.95f)
            };
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_EmissionMul"))
                mat.SetFloat("_EmissionMul", 0.72f);
            Nebulae[hueBucket] = mat;
            return mat;
        }

        public static Mesh AsteroidMesh(int seed)
        {
            EnsureAsteroidMeshes();
            return AsteroidMeshes[Mathf.Abs(seed) % AsteroidMeshes.Length];
        }

        public static void ApplyStarLightDirection(Vector3 worldLightFromStar)
        {
            var dir = worldLightFromStar.sqrMagnitude > 0.01f ? worldLightFromStar.normalized : Vector3.right;
            foreach (var mat in Planets.Values)
            {
                if (mat != null && mat.HasProperty("_LightDir"))
                    mat.SetVector("_LightDir", dir);
            }

            if (_asteroid != null && _asteroid.HasProperty("_LightDir"))
                _asteroid.SetVector("_LightDir", dir);
        }

        static StarKit BuildStar(string key)
        {
            var (color, emission, intensity, coronaScale) = StarLook(key);
            var surfaceShader = Shader.Find("SU/StarSurface") ?? Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            var coronaShader = Shader.Find("SU/StarCorona") ?? Shader.Find("SU/ParticleGlow") ?? Shader.Find("Sprites/Default");

            var surface = new Material(surfaceShader);
            if (_starTex != null && surface.HasProperty("_MainTex"))
                surface.mainTexture = _starTex;
            if (surface.HasProperty("_Color"))
                surface.SetColor("_Color", color);
            if (surface.HasProperty("_Emission"))
                surface.SetColor("_Emission", emission);
            if (surface.HasProperty("_EmissionMul"))
                surface.SetFloat("_EmissionMul", intensity);
            if (surface.HasProperty("_Flow"))
                surface.SetFloat("_Flow", key is "blue" or "purple" ? 0.12f : 0.07f);

            var corona = new Material(coronaShader);
            var glow = _softGlow != null ? _softGlow : _coronaTex;
            if (glow != null && corona.HasProperty("_MainTex"))
                corona.mainTexture = glow;
            if (corona.HasProperty("_Color"))
                corona.SetColor("_Color", color * 0.9f);
            if (corona.HasProperty("_EmissionMul"))
                corona.SetFloat("_EmissionMul", 0.95f);

            return new StarKit(surface, corona, color, intensity, coronaScale);
        }

        static Material MakePlanet(PlanetKind kind, Ownership own)
        {
            var shader = Shader.Find("SU/PlanetSurface") ?? Shader.Find("SU/HullMetal") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            var tex = PlanetTex.TryGetValue(kind, out var t) ? t : null;
            if (own == Ownership.Owned && PlanetTex.TryGetValue(PlanetKind.Terra, out var ownedTex))
            {
                // Prefer dedicated owned map when terra-like
                var owned = Resources.Load<Texture2D>("System/PlanetOwned");
                if (owned != null && kind is PlanetKind.Terra or PlanetKind.Desert)
                    tex = owned;
                else
                    tex = ownedTex;
            }

            if (tex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = tex;

            var albedo = kind switch
            {
                PlanetKind.Desert => new Color(1f, 0.92f, 0.82f),
                PlanetKind.Ice => new Color(0.9f, 0.95f, 1f),
                PlanetKind.Gas => new Color(1f, 0.95f, 0.85f),
                PlanetKind.Rock => new Color(0.85f, 0.8f, 0.75f),
                _ => Color.white
            };
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", albedo);

            var rim = own switch
            {
                Ownership.Owned => Cyan,
                Ownership.Foe => Amber,
                _ => ColdRim
            };
            var rimMul = own switch
            {
                Ownership.Owned => 0.85f,
                Ownership.Foe => 0.55f,
                _ => 0.22f
            };
            if (mat.HasProperty("_RimColor"))
                mat.SetColor("_RimColor", rim);
            if (mat.HasProperty("_RimMul"))
                mat.SetFloat("_RimMul", rimMul);
            if (mat.HasProperty("_LightColor"))
                mat.SetColor("_LightColor", new Color(1f, 0.92f, 0.75f));
            return mat;
        }

        static Material MakeAtmosphere(PlanetKind kind, Ownership own)
        {
            var shader = Shader.Find("SU/Atmosphere") ?? Shader.Find("SU/ParticleGlow") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            var baseCol = kind switch
            {
                PlanetKind.Desert => new Color(0.85f, 0.55f, 0.28f),
                PlanetKind.Ice => new Color(0.55f, 0.78f, 1f),
                PlanetKind.Gas => new Color(0.75f, 0.55f, 0.35f),
                PlanetKind.Rock => new Color(0.45f, 0.4f, 0.38f),
                _ => new Color(0.35f, 0.65f, 1f)
            };
            if (own == Ownership.Owned)
                baseCol = Color.Lerp(baseCol, Cyan, 0.35f);
            else if (own == Ownership.Foe)
                baseCol = Color.Lerp(baseCol, Amber, 0.28f);

            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_Intensity"))
                mat.SetFloat("_Intensity", kind == PlanetKind.Rock ? 0.55f : 1.25f);
            if (mat.HasProperty("_RimPower"))
                mat.SetFloat("_RimPower", kind == PlanetKind.Gas ? 2.2f : 2.9f);
            return mat;
        }

        static (Color color, Color emission, float intensity, float coronaScale) StarLook(string key)
        {
            return key switch
            {
                "blue" => (new Color(0.45f, 0.7f, 1f), new Color(0.55f, 0.75f, 1f), 2.1f, 1.22f),
                "red" => (new Color(1f, 0.42f, 0.32f), new Color(1f, 0.35f, 0.22f), 1.85f, 1.28f),
                "orange" => (new Color(1f, 0.68f, 0.32f), new Color(1f, 0.55f, 0.2f), 2.0f, 1.24f),
                "purple" or "violet" => (new Color(0.72f, 0.5f, 1f), new Color(0.65f, 0.4f, 1f), 2.05f, 1.26f),
                "green" => (new Color(0.4f, 1f, 0.6f), new Color(0.3f, 0.95f, 0.5f), 1.95f, 1.22f),
                "white" => (new Color(0.92f, 0.95f, 1f), new Color(0.85f, 0.9f, 1f), 1.75f, 1.18f),
                "yellow" => (new Color(1f, 0.9f, 0.55f), new Color(1f, 0.82f, 0.35f), 2.15f, 1.2f),
                _ => (new Color(1f, 0.88f, 0.55f), new Color(1f, 0.78f, 0.35f), 2.05f, 1.2f)
            };
        }

        static string NormalizeStarKey(string typeKey, int typeHash)
        {
            if (!string.IsNullOrEmpty(typeKey))
            {
                switch (typeKey.Trim().ToLowerInvariant())
                {
                    case "blue":
                    case "b":
                        return "blue";
                    case "red":
                    case "r":
                        return "red";
                    case "yellow":
                    case "y":
                        return "yellow";
                    case "white":
                    case "w":
                        return "white";
                    case "orange":
                    case "o":
                        return "orange";
                    case "purple":
                    case "violet":
                        return "purple";
                    case "green":
                    case "g":
                        return "green";
                }
            }

            return (Mathf.Abs(typeHash) % 5) switch
            {
                1 => "blue",
                2 => "red",
                3 => "orange",
                4 => "purple",
                _ => "yellow"
            };
        }

        static void EnsureTextures()
        {
            if (_starTex != null)
                return;
            _starTex = Resources.Load<Texture2D>("System/StarSurface");
            _coronaTex = Resources.Load<Texture2D>("System/Corona");
            _asteroidTex = Resources.Load<Texture2D>("System/Asteroid");
            _nebulaTex = Resources.Load<Texture2D>("System/Nebula");
            _softGlow = Resources.Load<Texture2D>("System/SoftGlow");
            PlanetTex[PlanetKind.Terra] = Resources.Load<Texture2D>("System/PlanetTerra");
            PlanetTex[PlanetKind.Desert] = Resources.Load<Texture2D>("System/PlanetDesert");
            PlanetTex[PlanetKind.Ice] = Resources.Load<Texture2D>("System/PlanetIce");
            PlanetTex[PlanetKind.Gas] = Resources.Load<Texture2D>("System/PlanetGas");
            PlanetTex[PlanetKind.Rock] = Resources.Load<Texture2D>("System/PlanetRock");
        }

        static void EnsureAsteroidMeshes()
        {
            if (_meshesReady)
                return;
            for (var i = 0; i < AsteroidMeshes.Length; i++)
                AsteroidMeshes[i] = BuildRockMesh(1100 + i * 97);
            _meshesReady = true;
        }

        static Mesh BuildRockMesh(int seed)
        {
            // Faceted rock from subdivided icosahedron + seeded displacement.
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0).normalized, new Vector3(1, t, 0).normalized,
                new Vector3(-1, -t, 0).normalized, new Vector3(1, -t, 0).normalized,
                new Vector3(0, -1, t).normalized, new Vector3(0, 1, t).normalized,
                new Vector3(0, -1, -t).normalized, new Vector3(0, 1, -t).normalized,
                new Vector3(t, 0, -1).normalized, new Vector3(t, 0, 1).normalized,
                new Vector3(-t, 0, -1).normalized, new Vector3(-t, 0, 1).normalized
            };
            var faces = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };

            // One subdivision
            var midCache = new Dictionary<long, int>();
            var newFaces = new List<int>();
            for (var f = 0; f < faces.Count; f += 3)
            {
                var a = faces[f];
                var b = faces[f + 1];
                var c = faces[f + 2];
                var ab = Mid(a, b, verts, midCache);
                var bc = Mid(b, c, verts, midCache);
                var ca = Mid(c, a, verts, midCache);
                newFaces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            faces = newFaces;
            var s = seed;
            for (var i = 0; i < verts.Count; i++)
            {
                var n = verts[i];
                var bump = 0.78f + 0.28f * Hash01(s + i * 13) + 0.12f * Hash01(s + i * 29);
                // flatten a few faces for crystalline look
                if (Hash01(s + i * 47) > 0.72f)
                    bump *= 0.88f;
                verts[i] = n * bump;
            }

            // Explode to unique face verts so shading stays faceted (flat)
            var outV = new List<Vector3>(faces.Count);
            var outN = new List<Vector3>(faces.Count);
            var outUv = new List<Vector2>(faces.Count);
            var outT = new List<int>(faces.Count);
            for (var f = 0; f < faces.Count; f += 3)
            {
                var a = verts[faces[f]];
                var b = verts[faces[f + 1]];
                var c = verts[faces[f + 2]];
                var n = Vector3.Cross(b - a, c - a).normalized;
                var i0 = outV.Count;
                outV.Add(a); outV.Add(b); outV.Add(c);
                outN.Add(n); outN.Add(n); outN.Add(n);
                outUv.Add(SphericalUv(a)); outUv.Add(SphericalUv(b)); outUv.Add(SphericalUv(c));
                outT.Add(i0); outT.Add(i0 + 1); outT.Add(i0 + 2);
            }

            var mesh = new Mesh { name = "SUAsteroid_" + seed };
            mesh.SetVertices(outV);
            mesh.SetNormals(outN);
            mesh.SetUVs(0, outUv);
            mesh.SetTriangles(outT, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Mid(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out var i))
                return i;
            var mid = ((verts[a] + verts[b]) * 0.5f).normalized;
            i = verts.Count;
            verts.Add(mid);
            cache[key] = i;
            return i;
        }

        static Vector2 SphericalUv(Vector3 n)
        {
            n = n.normalized;
            var u = 0.5f + Mathf.Atan2(n.z, n.x) / (Mathf.PI * 2f);
            var v = 0.5f - Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) / Mathf.PI;
            return new Vector2(u, v);
        }

        static float Hash01(int x)
        {
            var n = (uint)x * 747796405u + 2891336453u;
            n = ((n >> (int)((n >> 28) + 4)) ^ n) * 277803737u;
            n = (n >> 22) ^ n;
            return (n & 0xffff) / 65535f;
        }

        static int Stable(int id)
        {
            unchecked
            {
                var x = id * 73856093;
                x ^= x >> 13;
                x *= 19349663;
                return x;
            }
        }

        public readonly struct StarKit
        {
            public readonly Material Surface;
            public readonly Material Corona;
            public readonly Color Color;
            public readonly float Intensity;
            public readonly float CoronaScale;

            public StarKit(Material surface, Material corona, Color color, float intensity, float coronaScale)
            {
                Surface = surface;
                Corona = corona;
                Color = color;
                Intensity = intensity;
                CoronaScale = coronaScale;
            }
        }
    }
}
