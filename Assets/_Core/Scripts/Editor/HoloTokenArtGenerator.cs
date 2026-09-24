using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Core.Editor
{
    /// <summary>Regenerate crisp holomap radar glyphs with real alpha (no black matte).</summary>
    public static class HoloTokenArtGenerator
    {
        const int N = 256;
        const string Dir = "Assets/_Core/Resources/Holo";

        [MenuItem("StellarUniverse/Art/Regenerate Holo Tokens")]
        public static void Regenerate()
        {
            var cx = N * 0.5f;
            var cy = N * 0.5f;

            var fleet = Blank();
            FillPoly(fleet, new[]
            {
                new Vector2(cx, 28), new Vector2(cx + 52, 150), new Vector2(cx + 18, 150),
                new Vector2(cx + 18, 210), new Vector2(cx - 18, 210), new Vector2(cx - 18, 150),
                new Vector2(cx - 52, 150)
            }, new Color32(40, 220, 255, 255));
            FillCircle(fleet, cx, 218, 16, new Color32(180, 250, 255, 230));
            FillPoly(fleet, new[]
            {
                new Vector2(cx, 70), new Vector2(cx + 18, 130), new Vector2(cx - 18, 130)
            }, new Color32(200, 250, 255, 255));
            Save("TokenFleet.png", fleet);

            var amber = Blank();
            FillPoly(amber, new[]
            {
                new Vector2(cx, 28), new Vector2(cx + 52, 150), new Vector2(cx + 18, 150),
                new Vector2(cx + 18, 210), new Vector2(cx - 18, 210), new Vector2(cx - 18, 150),
                new Vector2(cx - 52, 150)
            }, new Color32(255, 140, 40, 255));
            FillCircle(amber, cx, 218, 16, new Color32(255, 200, 80, 230));
            FillPoly(amber, new[]
            {
                new Vector2(cx, 70), new Vector2(cx + 18, 130), new Vector2(cx - 18, 130)
            }, new Color32(255, 220, 160, 255));
            Save("TokenFleetAmber.png", amber);

            var rock = Blank();
            var pts = new Vector2[12];
            for (var i = 0; i < 12; i++)
            {
                var a = i / 12f * Mathf.PI * 2f;
                var rr = 78 + (i % 3 == 0 ? 22 : i % 2 == 0 ? -8 : 10);
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr);
            }

            FillPoly(rock, pts, new Color32(210, 195, 160, 255));
            FillCircle(rock, cx - 6, cy, 16, new Color32(120, 105, 85, 255));
            FillCircle(rock, cx + 28, cy + 18, 12, new Color32(140, 120, 95, 220));
            Save("TokenAsteroid.png", rock);

            var pad = Blank();
            StrokeCircle(pad, cx, cy, 100, 10, new Color32(60, 230, 255, 255));
            StrokeCircle(pad, cx, cy, 86, 3, new Color32(40, 180, 220, 140));
            Save("TokenPad.png", pad);

            var padE = Blank();
            StrokeCircle(padE, cx, cy, 100, 10, new Color32(255, 90, 50, 255));
            StrokeCircle(padE, cx, cy, 86, 3, new Color32(200, 60, 30, 140));
            Save("TokenPadEnemy.png", padE);

            AssetDatabase.Refresh();
            Debug.Log("[SU] Holo tokens regenerated with alpha.");
        }

        static Color32[] Blank()
        {
            var px = new Color32[N * N];
            for (var i = 0; i < px.Length; i++)
                px[i] = new Color32(0, 0, 0, 0);
            return px;
        }

        static void Save(string name, Color32[] px)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            var path = Path.Combine(Dir, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null)
                return;
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }

        static void Set(Color32[] px, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= N || y >= N)
                return;
            px[y * N + x] = c;
        }

        static void FillPoly(Color32[] px, Vector2[] pts, Color32 c)
        {
            var minY = 9999f;
            var maxY = -9999f;
            for (var i = 0; i < pts.Length; i++)
            {
                minY = Mathf.Min(minY, pts[i].y);
                maxY = Mathf.Max(maxY, pts[i].y);
            }

            for (var y = (int)minY; y <= (int)maxY; y++)
            {
                var xs = new List<float>();
                for (var i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
                    {
                        var t = (y - a.y) / (b.y - a.y);
                        xs.Add(a.x + t * (b.x - a.x));
                    }
                }

                xs.Sort();
                for (var i = 0; i + 1 < xs.Count; i += 2)
                {
                    var x0 = (int)xs[i];
                    var x1 = (int)xs[i + 1];
                    for (var x = x0; x <= x1; x++)
                        Set(px, x, y, c);
                }
            }
        }

        static void FillCircle(Color32[] px, float cx, float cy, float r, Color32 c)
        {
            var r0 = (int)(r + 1);
            var r2 = r * r;
            for (var y = (int)cy - r0; y <= (int)cy + r0; y++)
            for (var x = (int)cx - r0; x <= (int)cx + r0; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r2)
                    Set(px, x, y, c);
        }

        static void StrokeCircle(Color32[] px, float cx, float cy, float r, float w, Color32 c)
        {
            var r0 = (int)(r + w + 1);
            var r2 = r * r;
            var rin = (r - w) * (r - w);
            for (var y = (int)cy - r0; y <= (int)cy + r0; y++)
            for (var x = (int)cx - r0; x <= (int)cx + r0; x++)
            {
                var d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (d <= r2 && d >= rin)
                    Set(px, x, y, c);
            }
        }
    }
}
