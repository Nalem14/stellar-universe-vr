using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Procedural surfaces for the station's civil spaces (the concourse): a soft matte composite panel for walls
    /// and vault, a honed stone, and long boards for the concourse deck. Neutral in colour (tinted by the material), fine grain,
    /// drawn once, mipmapped, made non-readable. Seeded: every build looks the same.
    /// </summary>
    public static class StationSurfaces
    {
        static Texture2D _panel;
        static Texture2D _stone;
        static Texture2D _planks;

        /// <summary>Matte panel: a faint grain and long soft streaks (no edge trim; HullInterior cuts the seams).</summary>
        public static Texture2D Panel()
        {
            if (_panel != null)
                return _panel;
            const int n = 256;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var grain = Hash(x, y, 3) * 0.05f;
                var streak = Noise(u * 5f, v * 90f, 11, 5, 90) * 0.06f;
                var cloud = Noise(u * 4f, v * 4f, 5, 4, 4) * 0.08f;
                var k = Mathf.Clamp01(0.84f + grain + streak + cloud - 0.1f);
                var b = (byte)(k * 255f);
                px[y * n + x] = new Color32(b, b, b, 255);
            }

            _panel = Finish(px, n, "SU_StationPanel");
            return _panel;
        }

        /// <summary>Honed stone: warm clouds, a few thin veins, fine speckle.</summary>
        public static Texture2D Stone()
        {
            if (_stone != null)
                return _stone;
            const int n = 512;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var cloud = Noise(u * 6f, v * 6f, 21, 6, 6) * 0.6f + Noise(u * 17f, v * 17f, 22, 17, 17) * 0.4f;
                var warp = Noise(u * 3f, v * 3f, 23, 3, 3) * 4f;
                var vein = 1f - Mathf.Clamp01(Mathf.Abs(Mathf.Sin((u * 2f + v) * Mathf.PI * 2f + warp)) * 22f);
                vein *= Noise(u * 5f, v * 5f, 24, 5, 5);
                var speck = Hash(x, y, 9) > 0.985f ? -0.08f : 0f;
                var k = Mathf.Clamp01(0.72f + (cloud - 0.5f) * 0.2f - vein * 0.07f + speck + Hash(x, y, 4) * 0.03f);
                var c = new Color(k * 1.02f, k * 0.97f, k * 0.9f);
                px[y * n + x] = c;
            }

            _stone = Finish(px, n, "SU_StationStone");
            return _stone;
        }

        /// <summary>
        /// Long boards running along V: eight per tile across (0.25 m at 0.5 tiling), butt joints staggered per
        /// board, a tone per board length, fine grain streaks and a soft knot here and there.
        /// </summary>
        public static Texture2D Planks()
        {
            if (_planks != null)
                return _planks;
            const int n = 512;
            const int boards = 8;
            const int w = n / boards;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var col = x / w;
                var u = x / (float)n;
                var v = y / (float)n;
                // Two joints per board per tile, shifted by board.
                var shift = Hash(col, 0, 41);
                var run = v * 2f + shift;
                var piece = Mathf.FloorToInt(run);
                var along = run - piece;
                var tone = (Hash(col, (piece % 2 + 2) % 2, 43) - 0.5f) * 0.14f;
                var grain = Noise(u * 64f, v * 6f, 45, 64, 6) * 0.07f + Noise(u * 160f, v * 3f, 46, 160, 3) * 0.05f;
                var cloud = Noise(u * 8f, v * 4f, 47, 8, 4) * 0.06f;
                var k = 0.74f + tone + grain + cloud - 0.09f;
                // Board edges and butt joints: a thin dark line with a lighter bevel beside it.
                var ex = x % w;
                if (ex == 0) k *= 0.55f;
                else if (ex == 1) k *= 1.06f;
                var joint = Mathf.Min(along, 1f - along) * n * 0.5f;
                if (joint < 0.8f) k *= 0.6f;
                // A knot now and then.
                var kx = (col + 0.5f) * w;
                var ky = (piece + 0.5f - shift) * n * 0.5f;
                if (Hash(col, piece % 2, 49) > 0.7f)
                {
                    var d = new Vector2((x - kx) / 5f, (y - ky) / 11f).magnitude;
                    if (d < 1f)
                        k *= 0.82f + d * 0.18f;
                }

                k = Mathf.Clamp01(k);
                px[y * n + x] = new Color(k * 1.03f, k * 0.97f, k * 0.9f);
            }

            _planks = Finish(px, n, "SU_StationPlanks");
            return _planks;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
        }

        /// <summary>Value noise wrapping every <paramref name="px"/> × <paramref name="py"/> cells (the texture tiles).</summary>
        static float Noise(float x, float y, int seed, int px, int py)
        {
            var xi = Mathf.FloorToInt(x);
            var yi = Mathf.FloorToInt(y);
            var fx = x - xi;
            var fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float H(int a, int b) => Hash((a % px + px) % px, (b % py + py) % py, seed);
            var a0 = Mathf.Lerp(H(xi, yi), H(xi + 1, yi), fx);
            var a1 = Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), fx);
            return Mathf.Lerp(a0, a1, fy);
        }

        static Texture2D Finish(Color32[] px, int n, string name)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                name = name, filterMode = FilterMode.Trilinear, anisoLevel = 4, wrapMode = TextureWrapMode.Repeat
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
