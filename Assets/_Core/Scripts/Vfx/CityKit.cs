using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The city's art kit (<see cref="CityExterior"/>): a palette per kind of world, one tileable noise texture
    /// generated once (land detail, water ripples, clouds), the shared materials (one per surface family, never
    /// one per building) and <see cref="CityMesh"/>, the builder that packs a whole city into one mesh.
    /// </summary>
    public static class CityKit
    {
        /// <summary>One window bay across a facade, one storey up it (m): the windows' grid.</summary>
        public const float Bay = 3.6f;
        public const float Storey = 3.4f;

        public readonly struct Palette
        {
            public readonly Color FacadeA;
            public readonly Color FacadeB;
            public readonly Color Paving;
            public readonly Color GroundLow;
            public readonly Color GroundHigh;
            public readonly Color Rock;
            public readonly Color Snow;
            public readonly Color Zenith;
            public readonly Color Horizon;
            public readonly Color Cloud;
            public readonly float CloudCover;
            public readonly float Peaks;
            public readonly bool Sea;
            public readonly bool Gas;

            public Palette(Color facadeA, Color facadeB, Color paving, Color groundLow, Color groundHigh, Color rock,
                Color snow, Color zenith, Color horizon, Color cloud, float cloudCover, float peaks, bool sea, bool gas)
            {
                FacadeA = facadeA;
                FacadeB = facadeB;
                Paving = paving;
                GroundLow = groundLow;
                GroundHigh = groundHigh;
                Rock = rock;
                Snow = snow;
                Zenith = zenith;
                Horizon = horizon;
                Cloud = cloud;
                CloudCover = cloudCover;
                Peaks = peaks;
                Sea = sea;
                Gas = gas;
            }
        }

        /// <summary>The world's look: stone and glass of its city, its land, its sky (by <see cref="SystemBodyKit.PlanetKind"/>).</summary>
        public static Palette For(SystemBodyKit.PlanetKind kind) => kind switch
        {
            SystemBodyKit.PlanetKind.Desert => new Palette(
                new Color(0.86f, 0.74f, 0.58f), new Color(0.72f, 0.6f, 0.47f), new Color(0.62f, 0.55f, 0.45f),
                new Color(0.78f, 0.6f, 0.38f), new Color(0.7f, 0.5f, 0.3f), new Color(0.52f, 0.36f, 0.24f),
                new Color(0.92f, 0.86f, 0.76f), new Color(0.32f, 0.5f, 0.78f), new Color(0.93f, 0.8f, 0.62f),
                new Color(1f, 0.95f, 0.88f), 0.22f, 110f, false, false),
            SystemBodyKit.PlanetKind.Ice => new Palette(
                new Color(0.82f, 0.88f, 0.94f), new Color(0.66f, 0.74f, 0.82f), new Color(0.6f, 0.66f, 0.72f),
                new Color(0.84f, 0.9f, 0.97f), new Color(0.7f, 0.8f, 0.9f), new Color(0.44f, 0.52f, 0.62f),
                new Color(0.97f, 0.99f, 1f), new Color(0.3f, 0.5f, 0.82f), new Color(0.8f, 0.88f, 0.96f),
                new Color(0.95f, 0.98f, 1f), 0.5f, 210f, false, false),
            SystemBodyKit.PlanetKind.Rock => new Palette(
                new Color(0.7f, 0.68f, 0.66f), new Color(0.55f, 0.54f, 0.55f), new Color(0.48f, 0.46f, 0.45f),
                new Color(0.5f, 0.42f, 0.36f), new Color(0.44f, 0.34f, 0.28f), new Color(0.36f, 0.3f, 0.27f),
                new Color(0.82f, 0.8f, 0.78f), new Color(0.38f, 0.32f, 0.42f), new Color(0.78f, 0.62f, 0.52f),
                new Color(0.9f, 0.82f, 0.78f), 0.28f, 230f, false, false),
            SystemBodyKit.PlanetKind.Gas => new Palette(
                new Color(0.84f, 0.8f, 0.74f), new Color(0.66f, 0.62f, 0.58f), new Color(0.56f, 0.52f, 0.5f),
                new Color(0.9f, 0.86f, 0.84f), new Color(0.98f, 0.96f, 0.94f), new Color(0.86f, 0.72f, 0.66f),
                new Color(1f, 1f, 1f), new Color(0.42f, 0.38f, 0.62f), new Color(0.96f, 0.8f, 0.68f),
                new Color(1f, 0.92f, 0.82f), 0.62f, 140f, false, true),
            _ => new Palette(
                new Color(0.84f, 0.84f, 0.82f), new Color(0.66f, 0.7f, 0.72f), new Color(0.56f, 0.58f, 0.57f),
                new Color(0.34f, 0.5f, 0.28f), new Color(0.46f, 0.52f, 0.32f), new Color(0.42f, 0.4f, 0.38f),
                new Color(0.96f, 0.97f, 1f), new Color(0.24f, 0.46f, 0.84f), new Color(0.74f, 0.84f, 0.95f),
                new Color(1f, 1f, 1f), 0.42f, 170f, true, false)
        };

        // ── Noise ──────────────────────────────────────────────────────────────

        const int NoiseSize = 256;
        static Texture2D _noise;

        /// <summary>
        /// Tileable fractal value noise (256², single channel): the land's grain, the water's ripples, the sky's
        /// clouds. Generated once, uploaded non-readable.
        /// </summary>
        public static Texture2D Noise()
        {
            if (_noise != null)
                return _noise;
            var px = new Color32[NoiseSize * NoiseSize];
            var rng = new System.Random(4242);
            var lattice = new float[5][];
            int[] periods = { 4, 8, 16, 32, 64 };
            for (var o = 0; o < periods.Length; o++)
            {
                var p = periods[o];
                lattice[o] = new float[p * p];
                for (var i = 0; i < lattice[o].Length; i++)
                    lattice[o][i] = (float)rng.NextDouble();
            }

            for (var y = 0; y < NoiseSize; y++)
            for (var x = 0; x < NoiseSize; x++)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                for (var o = 0; o < periods.Length; o++)
                {
                    sum += Lattice(lattice[o], periods[o], x / (float)NoiseSize, y / (float)NoiseSize) * amp;
                    norm += amp;
                    amp *= 0.55f;
                }

                var v = (byte)Mathf.Clamp(Mathf.RoundToInt(sum / norm * 255f), 0, 255);
                px[y * NoiseSize + x] = new Color32(v, v, v, 255);
            }

            _noise = new Texture2D(NoiseSize, NoiseSize, TextureFormat.RGBA32, true)
            {
                name = "SU_CityNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 2
            };
            _noise.SetPixels32(px);
            _noise.Apply(true, true);
            return _noise;
        }

        static float Lattice(float[] grid, int period, float u, float v)
        {
            var x = u * period;
            var y = v * period;
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float At(int i, int j) => grid[((j % period + period) % period) * period + (i % period + period) % period];
            var a = Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx);
            var b = Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        // ── Materials (one per family, shared by every city) ─────────────────────

        static Material _blocks;
        static Material _land;
        static Material _cloudSea;
        static Material _water;
        static Material _sky;
        static Material _dome;
        static Material _traffic;

        static Material Make(string shaderName, string name, string fallback = "SU/UnlitEmissive")
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning("[SU] CityKit: " + shaderName + " missing, falling back to " + fallback + ".");
                shader = Shader.Find(fallback) ?? Shader.Find("Unlit/Color");
            }

            return new Material(shader) { name = name };
        }

        public static Material Blocks() => _blocks ??= Make("SU/CityBlock", "SU_CityBlocks", "SU/HullInterior");

        public static Material Land(bool gas)
        {
            if (gas)
            {
                if (_cloudSea != null)
                    return _cloudSea;
                _cloudSea = Make("SU/CityGround", "SU_CityCloudSea");
                _cloudSea.SetTexture("_Noise", Noise());
                _cloudSea.SetFloat("_Gas", 1f);
                _cloudSea.SetFloat("_DetailScale", 160f);
                _cloudSea.SetFloat("_Detail", 0.45f);
                return _cloudSea;
            }

            if (_land != null)
                return _land;
            _land = Make("SU/CityGround", "SU_CityLand");
            _land.SetTexture("_Noise", Noise());
            return _land;
        }

        public static Material Water()
        {
            if (_water != null)
                return _water;
            _water = Make("SU/CityWater", "SU_CityWater");
            _water.SetTexture("_Noise", Noise());
            return _water;
        }

        /// <summary>The sky: one material per kind is not needed — its colours are set when a city is built.</summary>
        public static Material Sky()
        {
            if (_sky != null)
                return _sky;
            _sky = Make("SU/PlanetSky", "SU_PlanetSky");
            _sky.SetTexture("_Noise", Noise());
            return _sky;
        }

        public static Material Dome() => _dome ??= Make("SU/ShieldDome", "SU_ShieldDome");

        /// <summary>Air traffic lights: the shared additive glow, its own copy (vertex colours carry the hue).</summary>
        public static Material Traffic()
        {
            if (_traffic != null)
                return _traffic;
            _traffic = new Material(CombatFxKit.Glow()) { name = "SU_CityTraffic" };
            return _traffic;
        }
    }

    /// <summary>
    /// Packs city geometry into one mesh for SU/CityBlock: buildings (facade window grid in uv0), roofs, and
    /// self-lit light bands (boulevards, trims, beacons, pads). uv1 = (seed, lit share, roof, band). Every face
    /// is oriented against the normal it is given, so callers list corners in any order.
    /// </summary>
    public sealed class CityMesh
    {
        readonly List<Vector3> _v = new(4096);
        readonly List<Vector3> _n = new(4096);
        readonly List<Color32> _c = new(4096);
        readonly List<Vector2> _uv = new(4096);
        readonly List<Vector4> _d = new(4096);
        readonly List<int> _t = new(8192);

        public int Vertices => _v.Count;

        void Vert(Vector3 p, Vector3 n, Color32 c, Vector2 uv, Vector4 d)
        {
            _v.Add(p);
            _n.Add(n);
            _c.Add(c);
            _uv.Add(uv);
            _d.Add(d);
        }

        /// <summary>A quad a-b-c-d (a loop), facing <paramref name="n"/>.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col, Vector2 uvA, Vector2 uvC, Vector4 data)
        {
            var i = _v.Count;
            Vert(a, n, col, uvA, data);
            Vert(b, n, col, new Vector2(uvC.x, uvA.y), data);
            Vert(c, n, col, uvC, data);
            Vert(d, n, col, new Vector2(uvA.x, uvC.y), data);
            var flip = Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f;
            if (!flip)
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

        static Vector4 Data(float seed, float lit, bool roof, bool band) =>
            new(seed, lit, roof ? 1f : 0f, band ? 1f : 0f);

        /// <summary>
        /// A block: footprint <paramref name="w"/> × <paramref name="d"/> on <paramref name="foot"/>, <paramref name="h"/>
        /// high, turned <paramref name="yaw"/>°; four facades with their window grid and a roof.
        /// </summary>
        public void Box(Vector3 foot, float w, float d, float h, float yaw, Color32 col, float seed, float lit, bool band = false)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var hx = w * 0.5f;
            var hz = d * 0.5f;
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);
            var data = Data(seed, lit, false, band);
            var u0 = seed * 7.3f;
            var top = h / CityKit.Storey;
            Side(P(-hx, 0, -hz), P(hx, 0, -hz), P(hx, h, -hz), P(-hx, h, -hz), rot * Vector3.back, w);
            Side(P(hx, 0, -hz), P(hx, 0, hz), P(hx, h, hz), P(hx, h, -hz), rot * Vector3.right, d);
            Side(P(hx, 0, hz), P(-hx, 0, hz), P(-hx, h, hz), P(hx, h, hz), rot * Vector3.forward, w);
            Side(P(-hx, 0, hz), P(-hx, 0, -hz), P(-hx, h, -hz), P(-hx, h, hz), rot * Vector3.left, d);
            Quad(P(-hx, h, -hz), P(hx, h, -hz), P(hx, h, hz), P(-hx, h, hz), Vector3.up, col,
                new Vector2(0f, 0f), new Vector2(w / CityKit.Bay, d / CityKit.Bay), Data(seed, lit, true, band));

            void Side(Vector3 a, Vector3 b, Vector3 c, Vector3 e, Vector3 n, float len) =>
                Quad(a, b, c, e, n, col, new Vector2(u0, 0f), new Vector2(u0 + len / CityKit.Bay, top), data);
        }

        /// <summary>A round or tapered tower (prism of <paramref name="sides"/>), radius r0 at the foot, r1 at the top.</summary>
        public void Prism(Vector3 foot, float r0, float r1, float h, int sides, float yaw, Color32 col, float seed, float lit,
            bool band = false, bool cap = true)
        {
            var data = Data(seed, lit, false, band);
            var u = seed * 7.3f;
            var top = h / CityKit.Storey;
            for (var i = 0; i < sides; i++)
            {
                var a0 = (yaw + i * 360f / sides) * Mathf.Deg2Rad;
                var a1 = (yaw + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                var chord = (d1 - d0).magnitude * r0;
                var n = (d0 + d1).normalized;
                n.y = (r0 - r1) / Mathf.Max(h, 0.01f);
                n.Normalize();
                Quad(foot + d0 * r0, foot + d1 * r0, foot + d1 * r1 + Vector3.up * h, foot + d0 * r1 + Vector3.up * h, n, col,
                    new Vector2(u, 0f), new Vector2(u + chord / CityKit.Bay, top), data);
                u += chord / CityKit.Bay;
            }

            if (!cap || r1 <= 0.01f)
                return;
            var c = foot + Vector3.up * h;
            for (var i = 0; i < sides; i++)
            {
                var a0 = (yaw + i * 360f / sides) * Mathf.Deg2Rad;
                var a1 = (yaw + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                var p0 = c + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * r1;
                var p1 = c + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * r1;
                Quad(c, p0, p1, c, Vector3.up, col, Vector2.zero, new Vector2(r1 / CityKit.Bay, r1 / CityKit.Bay),
                    Data(seed, lit, true, band));
            }
        }

        /// <summary>A dome (half sphere) — greenhouses, labs; <paramref name="band"/> = it glows by itself.</summary>
        public void Dome(Vector3 centre, float r, int sides, int rings, Color32 col, bool band, float squash = 1f)
        {
            var data = Data(0f, 0f, true, band);
            for (var j = 0; j < rings; j++)
            {
                var t0 = j / (float)rings * Mathf.PI * 0.5f;
                var t1 = (j + 1) / (float)rings * Mathf.PI * 0.5f;
                for (var i = 0; i < sides; i++)
                {
                    var a0 = i * Mathf.PI * 2f / sides;
                    var a1 = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 D(float a, float t) => new(Mathf.Cos(t) * Mathf.Sin(a), Mathf.Sin(t) * squash, Mathf.Cos(t) * Mathf.Cos(a));
                    var p00 = centre + D(a0, t0) * r;
                    var p10 = centre + D(a1, t0) * r;
                    var p11 = centre + D(a1, t1) * r;
                    var p01 = centre + D(a0, t1) * r;
                    var n = D((a0 + a1) * 0.5f, (t0 + t1) * 0.5f).normalized;
                    Quad(p00, p10, p11, p01, n, col, Vector2.zero, Vector2.one, data);
                }
            }
        }

        /// <summary>A flat ring on the ground (roads, pad markings) at height <paramref name="y"/> over <paramref name="centre"/>.</summary>
        public void Annulus(Vector3 centre, float r0, float r1, int sides, Color32 col, bool band)
        {
            var data = Data(0f, 0f, true, band);
            for (var i = 0; i < sides; i++)
            {
                var a0 = i * Mathf.PI * 2f / sides;
                var a1 = (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                Quad(centre + d0 * r0, centre + d1 * r0, centre + d1 * r1, centre + d0 * r1, Vector3.up, col,
                    Vector2.zero, Vector2.one, data);
            }
        }

        /// <summary>A flat strip from a to b (boulevard light lines), <paramref name="width"/> wide.</summary>
        public void Strip(Vector3 a, Vector3 b, float width, Color32 col, bool band = true)
        {
            var dir = b - a;
            dir.y = 0f;
            var side = Vector3.Cross(Vector3.up, dir.normalized) * (width * 0.5f);
            Quad(a - side, b - side, b + side, a + side, Vector3.up, col, Vector2.zero, Vector2.one, Data(0f, 0f, true, band));
        }

        /// <summary>A beam between two points (crane jibs, gate frames): a square tube, no windows.</summary>
        public void Beam(Vector3 a, Vector3 b, float thick, Color32 col, bool band = false)
        {
            var axis = (b - a).normalized;
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized * (thick * 0.5f);
            var v = Vector3.Cross(axis, u).normalized * (thick * 0.5f);
            var data = Data(0f, 0f, true, band);
            Vector3[] o = { u + v, -u + v, -u - v, u - v };
            for (var i = 0; i < 4; i++)
            {
                var o0 = o[i];
                var o1 = o[(i + 1) % 4];
                Quad(a + o0, a + o1, b + o1, b + o0, (o0 + o1).normalized, col, Vector2.zero, Vector2.one, data);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000)
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetColors(_c);
            m.SetUVs(0, _uv);
            m.SetUVs(1, _d);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }
    }
}
