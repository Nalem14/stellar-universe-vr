using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.UI
{
    /// <summary>Empire flag as the server stores it: {"bg":"#rrggbb","shapes":[{"shape","color"}×3]}.</summary>
    [Serializable]
    public sealed class FlagSpec
    {
        public static readonly string[] Shapes = { "none", "circle", "triangle", "star", "diamond", "stripe", "ring", "cross" };

        public string Bg = "#001f3f";
        public readonly string[] Shape = { "circle", "none", "none" };
        public readonly string[] Color = { "#ff4136", "#2ecc40", "#ffdc00" };

        public string ToJson()
        {
            var shapes = new JArray();
            for (var i = 0; i < 3; i++)
                shapes.Add(new JObject { ["shape"] = Shape[i], ["color"] = Color[i] });
            return new JObject { ["bg"] = Bg, ["shapes"] = shapes }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static FlagSpec FromJson(string json)
        {
            var f = new FlagSpec();
            if (string.IsNullOrEmpty(json))
                return f;
            try
            {
                var o = JObject.Parse(json);
                f.Bg = (string)o["bg"] ?? f.Bg;
                if (o["shapes"] is JArray arr)
                {
                    for (var i = 0; i < 3 && i < arr.Count; i++)
                    {
                        f.Shape[i] = (string)arr[i]["shape"] ?? "none";
                        f.Color[i] = (string)arr[i]["color"] ?? f.Color[i];
                    }
                }
            }
            catch
            {
                // Default flag, like web empireFlag.js parseFlag.
            }

            return f;
        }
    }

    /// <summary>
    /// Paints a <see cref="FlagSpec"/> into a texture exactly like web scripts/empireFlag.js drawFlag
    /// (centred shapes of radius 60 / 40 / 20 on a 200-unit square, scaled to the texture), with 1-px
    /// antialiasing from signed distances. Repaints into the same texture: no allocation per edit.
    /// </summary>
    public static class FlagPainter
    {
        public const int Width = 300;
        public const int Height = 200;
        static readonly float[] Sizes = { 60f, 40f, 20f };
        static Color32[] _pixels;

        public static Texture2D Paint(FlagSpec flag, Texture2D into = null)
        {
            if (into == null || into.width != Width || into.height != Height)
            {
                into = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                {
                    name = "EmpireFlag",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            if (_pixels == null || _pixels.Length != Width * Height)
                _pixels = new Color32[Width * Height];

            var bg = Hex(flag.Bg, new Color(0f, 0.12f, 0.25f));
            var scale = Mathf.Min(Width, Height) / 200f;
            var cx = Width * 0.5f;
            var cy = Height * 0.5f;
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var c = bg;
                    // Canvas y grows downward; texture rows grow upward — flip so the star points up.
                    var px = x + 0.5f - cx;
                    var py = (Height - 1 - y) + 0.5f - cy;
                    for (var i = 0; i < 3; i++)
                    {
                        var shape = flag.Shape[i];
                        if (string.IsNullOrEmpty(shape) || shape == "none")
                            continue;
                        var d = Distance(shape, px, py, Sizes[i] * scale);
                        var a = Mathf.Clamp01(0.5f - d);
                        if (a > 0f)
                            c = Color.Lerp(c, Hex(flag.Color[i], Color.white), a);
                    }

                    _pixels[y * Width + x] = c;
                }
            }

            into.SetPixels32(_pixels);
            into.Apply(false, false);
            return into;
        }

        /// <summary>Signed distance (px) to the shape centred at the origin, canvas orientation (y down).</summary>
        static float Distance(string shape, float x, float y, float size)
        {
            switch (shape)
            {
                case "circle":
                    return Mathf.Sqrt(x * x + y * y) - size;
                case "ring":
                {
                    // ctx.lineWidth = size / 4 stroked on the circle.
                    var r = Mathf.Sqrt(x * x + y * y);
                    return Mathf.Abs(r - size) - size / 8f;
                }
                case "diamond":
                    return (Mathf.Abs(x) + Mathf.Abs(y) - size) * 0.70710678f;
                case "stripe":
                    return Box(x, y, size, size / 4f);
                case "cross":
                    // Two bars: x −size/5..+size/5 (width size/2.5), full height; and the transposed one.
                    return Mathf.Min(Box(x - 0f, y, size / 5f, size), Box(x, y - 0f, size, size / 5f));
                case "triangle":
                    return Triangle(x, y, size);
                case "star":
                    return Star(x, y, size, size * 0.5f);
                default:
                    return 1e6f;
            }
        }

        static float Box(float x, float y, float hx, float hy)
        {
            var dx = Mathf.Abs(x) - hx;
            var dy = Mathf.Abs(y) - hy;
            var outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(dx, dy), 0f);
        }

        // Canvas triangle: apex (0, −size), base (±size, +size).
        static float Triangle(float x, float y, float size)
        {
            var a = new Vector2(0f, -size);
            var b = new Vector2(-size, size);
            var c = new Vector2(size, size);
            return Polygon(new Vector2(x, y), a, b, c);
        }

        static readonly Vector2[] StarPts = new Vector2[10];

        static float Star(float x, float y, float outer, float inner)
        {
            var step = Mathf.PI / 5f;
            for (var i = 0; i < 10; i++)
            {
                var r = i % 2 == 0 ? outer : inner;
                var ang = i * step - Mathf.PI / 2f;
                StarPts[i] = new Vector2(r * Mathf.Cos(ang), r * Mathf.Sin(ang));
            }

            return Polygon(new Vector2(x, y), StarPts);
        }

        /// <summary>Signed distance to a simple polygon (even-odd inside test).</summary>
        static float Polygon(Vector2 p, params Vector2[] v)
        {
            var d = float.MaxValue;
            var inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, (w - e * t).sqrMagnitude);
                if ((v[i].y > p.y) != (v[j].y > p.y) &&
                    p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x)
                    inside = !inside;
            }

            var dist = Mathf.Sqrt(d);
            return inside ? -dist : dist;
        }

        public static Color Hex(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
