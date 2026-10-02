using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Procedural screen faces for the station's data walls and desks (drawn once, mipmapped, then made
    /// non-readable): tiled telemetry panels — bar charts, sparklines, radial gauges, ruled readouts — in the
    /// station's cyan on deep navy, with amber warnings here and there. Seeded, so every build looks the same.
    /// </summary>
    public static class StationScreens
    {
        static Texture2D _wall;
        static Texture2D _console;

        static readonly Color32 Ground = new(5, 13, 24, 255);
        static readonly Color32 Grid = new(12, 34, 52, 255);
        static readonly Color32 Line = new(70, 205, 245, 255);
        static readonly Color32 Dim = new(28, 96, 128, 255);
        static readonly Color32 Hot = new(255, 170, 70, 255);
        static readonly Color32 White = new(205, 240, 255, 255);

        public static Texture2D DataWall()
        {
            if (_wall != null)
                return _wall;
            const int w = 1024, h = 256;
            var px = Canvas(w, h, 16);
            var rnd = new System.Random(4207);
            var x = 6;
            while (x < w - 40)
            {
                var pw = 90 + rnd.Next(70);
                pw = Mathf.Min(pw, w - 6 - x);
                Panel(px, w, h, x, 8, pw, h - 16, rnd);
                x += pw + 8;
            }

            _wall = Finish(px, w, h, "SU_StationDataWall");
            _wall.wrapMode = TextureWrapMode.Repeat;
            return _wall;
        }

        public static Texture2D Console()
        {
            if (_console != null)
                return _console;
            const int w = 512, h = 64;
            var px = Canvas(w, h, 0);
            var rnd = new System.Random(911);
            // Key rows: rounded-ish pads, a few lit, and a waveform strip on the right third.
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 18; col++)
            {
                var kx = 8 + col * 18;
                var ky = 8 + row * 18;
                var c = rnd.NextDouble() < 0.18 ? (rnd.NextDouble() < 0.25 ? Hot : Line) : Dim;
                Rect(px, w, h, kx, ky, 14, 12, c, true);
            }

            Rect(px, w, h, 340, 8, 164, 48, Dim, false);
            var prev = 32;
            for (var i = 0; i < 160; i++)
            {
                var y = 32 + Mathf.RoundToInt(Mathf.Sin(i * 0.21f) * 12f * Mathf.Sin(i * 0.037f + 1f));
                VLine(px, w, h, 342 + i, Mathf.Min(prev, y), Mathf.Max(prev, y) + 1, Line);
                prev = y;
            }

            _console = Finish(px, w, h, "SU_StationConsole");
            return _console;
        }

        static Texture2D _pane;

        /// <summary>
        /// A concourse window seen from outside: warm cove light along the top fading into the hall, the vault's
        /// ribs, the sill, and the dark backs of benches and people against it (u along the ring, v up).
        /// </summary>
        public static Texture2D RingPane()
        {
            if (_pane != null)
                return _pane;
            const int w = 128, h = 64;
            var px = new Color32[w * h];
            var rnd = new System.Random(1717);
            for (var y = 0; y < h; y++)
            {
                var v = y / (float)(h - 1);
                // Brightest under the cove (top), a warm glow mid-hall, darker toward the floor.
                var k = Mathf.Lerp(0.22f, 1f, Mathf.Pow(v, 1.6f));
                var c = Color.Lerp(new Color(0.16f, 0.1f, 0.06f), new Color(1f, 0.82f, 0.58f), k);
                for (var x = 0; x < w; x++)
                    px[y * w + x] = c;
            }

            var shade = new Color32(26, 18, 12, 255);
            // Sill band and the vault's ribs.
            Rect(px, w, h, 0, 0, w, 7, shade, true);
            for (var x = 0; x < w; x += 32)
                Rect(px, w, h, x, 7, 2, h - 7, new Color32(60, 42, 28, 255), true);
            // Benches and a few figures, back-lit.
            for (var b = 0; b < 3; b++)
                Rect(px, w, h, 8 + b * 42, 7, 24, 5, shade, true);
            for (var f = 0; f < 4; f++)
            {
                var fx = 6 + rnd.Next(w - 12);
                var fh = 16 + rnd.Next(6);
                Rect(px, w, h, fx, 7, 3, fh, shade, true);
                Rect(px, w, h, fx - 1, 7 + fh, 5, 4, shade, true);
            }

            _pane = Finish(px, w, h, "SU_StationRingPane");
            return _pane;
        }

        // ── Drawing ─────────────────────────────────────────────────────────────

        static Color32[] Canvas(int w, int h, int grid)
        {
            var px = new Color32[w * h];
            for (var i = 0; i < px.Length; i++)
                px[i] = Ground;
            if (grid > 0)
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (x % grid == 0 || y % grid == 0)
                        px[y * w + x] = Grid;
            return px;
        }

        static void Panel(Color32[] px, int w, int h, int x0, int y0, int pw, int ph, System.Random rnd)
        {
            Rect(px, w, h, x0, y0, pw, ph, Dim, false);
            // Header rule and a title bar of "text".
            HLine(px, w, h, x0 + 4, x0 + pw - 4, y0 + ph - 18, Dim);
            Dashes(px, w, h, x0 + 8, y0 + ph - 12, pw - 16, rnd, White, 1);
            var kind = rnd.Next(4);
            var ix = x0 + 8;
            var iy = y0 + 10;
            var iw = pw - 16;
            var ih = ph - 40;
            switch (kind)
            {
                case 0:
                    // Bar chart.
                    var bars = Mathf.Max(4, iw / 9);
                    for (var b = 0; b < bars; b++)
                    {
                        var bh = (int)(ih * (0.2 + rnd.NextDouble() * 0.75));
                        Rect(px, w, h, ix + b * 9, iy, 6, bh, rnd.NextDouble() < 0.1 ? Hot : Line, true);
                    }

                    break;
                case 1:
                    // Sparklines.
                    for (var s = 0; s < 3; s++)
                    {
                        var baseY = iy + 12 + s * (ih / 3);
                        var prev = baseY;
                        var phase = rnd.NextDouble() * 6.0;
                        for (var i = 0; i < iw; i++)
                        {
                            var y = baseY + (int)(Mathf.Sin((float)(i * 0.11 + phase)) * 9f + Mathf.Sin((float)(i * 0.031 + phase * 2)) * 6f);
                            VLine(px, w, h, ix + i, Mathf.Min(prev, y), Mathf.Max(prev, y) + 1, s == 1 ? White : Line);
                            prev = y;
                        }
                    }

                    break;
                case 2:
                    // Radial gauges.
                    var r = Mathf.Min(iw / 2, ih) / 2 - 4;
                    var cx = ix + iw / 2;
                    var cy = iy + ih / 2;
                    Ring(px, w, h, cx, cy, r, Dim, 1f);
                    Ring(px, w, h, cx, cy, r - 6, Line, (float)(0.35 + rnd.NextDouble() * 0.6));
                    Ring(px, w, h, cx, cy, r - 14, rnd.NextDouble() < 0.3 ? Hot : White, (float)(0.2 + rnd.NextDouble() * 0.7));
                    HLine(px, w, h, cx - r, cx + r, cy, Grid);
                    VLine(px, w, h, cx, cy - r, cy + r, Grid);
                    break;
                default:
                    // Ruled readout.
                    for (var y = iy + ih - 8; y > iy; y -= 11)
                        Dashes(px, w, h, ix, y, iw, rnd, rnd.NextDouble() < 0.12 ? Hot : Line, 2);
                    break;
            }
        }

        static void Dashes(Color32[] px, int w, int h, int x0, int y, int width, System.Random rnd, Color32 c, int thick)
        {
            var x = x0;
            while (x < x0 + width - 6)
            {
                var len = 3 + rnd.Next(14);
                len = Mathf.Min(len, x0 + width - x);
                Rect(px, w, h, x, y, len, thick + 1, c, true);
                x += len + 3 + rnd.Next(5);
            }
        }

        static void Rect(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 c, bool fill)
        {
            for (var y = y0; y < y0 + rh; y++)
            for (var x = x0; x < x0 + rw; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h)
                    continue;
                if (fill || x == x0 || y == y0 || x == x0 + rw - 1 || y == y0 + rh - 1)
                    px[y * w + x] = c;
            }
        }

        static void HLine(Color32[] px, int w, int h, int x0, int x1, int y, Color32 c)
        {
            if (y < 0 || y >= h)
                return;
            for (var x = Mathf.Max(0, x0); x <= Mathf.Min(w - 1, x1); x++)
                px[y * w + x] = c;
        }

        static void VLine(Color32[] px, int w, int h, int x, int y0, int y1, Color32 c)
        {
            if (x < 0 || x >= w)
                return;
            for (var y = Mathf.Max(0, y0); y <= Mathf.Min(h - 1, y1); y++)
                px[y * w + x] = c;
        }

        static void Ring(Color32[] px, int w, int h, int cx, int cy, int r, Color32 c, float fill)
        {
            if (r <= 2)
                return;
            var steps = Mathf.CeilToInt(r * 6.3f * fill);
            for (var i = 0; i < steps; i++)
            {
                var a = Mathf.PI * 0.5f - i / (float)Mathf.Max(1, steps) * Mathf.PI * 2f * fill;
                for (var t = 0; t < 2; t++)
                {
                    var x = cx + Mathf.RoundToInt(Mathf.Cos(a) * (r - t));
                    var y = cy + Mathf.RoundToInt(Mathf.Sin(a) * (r - t));
                    if (x >= 0 && y >= 0 && x < w && y < h)
                        px[y * w + x] = c;
                }
            }
        }

        static Texture2D Finish(Color32[] px, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Slides a data wall's telemetry sideways (one shared material, one property a frame).</summary>
        public sealed class Scroll : MonoBehaviour
        {
            Material _mat;
            float _speed;

            public void Bind(Material mat, float speed)
            {
                _mat = mat;
                _speed = speed;
            }

            void Update()
            {
                if (_mat != null)
                    _mat.mainTextureOffset = new Vector2(Time.time * _speed % 1f, 0f);
            }
        }
    }
}
