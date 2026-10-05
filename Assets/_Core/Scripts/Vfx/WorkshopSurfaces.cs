using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Procedural surfaces for working spaces, so each part of a room reads as its own material instead of one panel
    /// everywhere. The dry dock's workshop: diamond tread plate underfoot, ribbed wall cladding, an open ceiling
    /// grating, worn paint for cabinets and tanks, yellow-and-black hazard stripes. The research lab's clean room:
    /// large floor tiles, white wall panels with shadow gaps, perforated acoustic ceiling tiles. Greyscale (tinted by
    /// the material) except the hazard stripes; 256 px, tiling, generated once and shared.
    /// </summary>
    public static class WorkshopSurfaces
    {
        static Texture2D _tread;
        static Texture2D _ribbed;
        static Texture2D _grating;
        static Texture2D _paint;
        static Texture2D _hazard;
        static Texture2D _labTile;
        static Texture2D _labPanel;
        static Texture2D _acoustic;
        static readonly System.Collections.Generic.Dictionary<string, Material> Tiles = new();

        /// <summary>
        /// A surface laid at its real size on one stretched box: the texture repeats <paramref name="repeat"/> times
        /// across the face. Shared per (texture, tint, emission, repeat).
        /// </summary>
        public static Material Tiled(CicArtKit art, Texture tex, Color tint, float emission, Vector2 repeat)
        {
            var key = tex.GetInstanceID() + "|" + tint + "|" + emission + "|" + repeat;
            if (Tiles.TryGetValue(key, out var mat) && mat != null)
                return mat;
            mat = new Material(art.Lit(tex, tint, emission)) { name = "SU_Tiled_" + tex.name };
            mat.SetTextureScale("_MainTex", repeat);
            Tiles[key] = mat;
            return mat;
        }

        static float Hash(int x, int y, int seed) => StationSurfaces.Hash(x, y, seed);
        static float Noise(float x, float y, int seed, int px, int py) => StationSurfaces.Noise(x, y, seed, px, py);

        static Color32 Grey(float k)
        {
            var b = (byte)(Mathf.Clamp01(k) * 255f);
            return new Color32(b, b, b, 255);
        }

        /// <summary>Diamond tread plate: rows of raised lozenges, alternately slanted, on brushed steel.</summary>
        public static Texture2D TreadPlate()
        {
            if (_tread != null)
                return _tread;
            const int n = 256;
            const int cell = 32;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var k = 0.62f + Noise(u * 6f, v * 6f, 61, 6, 6) * 0.08f + Noise(u * 200f, v * 4f, 62, 200, 4) * 0.05f
                        + Hash(x, y, 63) * 0.03f;
                // One lozenge per cell, slanted ±45° by the cell's checker, raised: lit top-left, shaded bottom-right.
                var cx = x / cell;
                var cy = y / cell;
                var lx = (x % cell - cell * 0.5f) / (cell * 0.5f);
                var ly = (y % cell - cell * 0.5f) / (cell * 0.5f);
                var slant = (cx + cy) % 2 == 0 ? 1f : -1f;
                var along = (lx + slant * ly) * 0.7071f;
                var across = (lx - slant * ly) * 0.7071f;
                var d = Mathf.Abs(along) / 0.62f + Mathf.Abs(across) / 0.12f;
                if (d < 1f)
                {
                    var light = (-lx - ly) * 0.5f;
                    k += 0.12f + light * 0.14f * (1f - d);
                    if (d > 0.82f)
                        k -= 0.06f;
                }

                // Wear: the raised bars polish where boots go.
                k += Noise(u * 3f, v * 3f, 64, 3, 3) * 0.05f;
                px[y * n + x] = Grey(k);
            }

            _tread = StationSurfaces.Finish(px, n, "SU_WorkshopTread");
            return _tread;
        }

        /// <summary>Ribbed cladding: vertical standing seams every 1/4 tile with a bolt row at the top and bottom.</summary>
        public static Texture2D Ribbed()
        {
            if (_ribbed != null)
                return _ribbed;
            const int n = 256;
            const int rib = 64;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var k = 0.78f + Noise(u * 4f, v * 30f, 71, 4, 30) * 0.06f + Hash(x, y, 72) * 0.025f;
                var rx = x % rib;
                // A shallow fluting across each panel, a raised seam at its edge.
                k += Mathf.Sin(rx / (float)rib * Mathf.PI * 4f) * 0.035f;
                if (rx < 3)
                    k = rx == 0 ? k * 0.55f : rx == 1 ? k * 1.12f : k * 0.92f;
                // Bolts near the panel's top and bottom.
                foreach (var by in new[] { 14, n - 14 })
                {
                    var dx = rx - rib * 0.5f;
                    var dy = y - by;
                    var r2 = dx * dx + dy * dy;
                    if (r2 < 16f)
                        k = 0.95f - (dx + dy) * 0.03f;
                    else if (r2 < 25f)
                        k *= 0.7f;
                }

                // A horizontal joint at mid height.
                if (y == n / 2)
                    k *= 0.6f;
                else if (y == n / 2 + 1)
                    k *= 1.08f;
                px[y * n + x] = Grey(k);
            }

            _ribbed = StationSurfaces.Finish(px, n, "SU_WorkshopRibbed");
            return _ribbed;
        }

        /// <summary>Open steel grating: a lattice of flat bars over darkness (the plenum above the lights).</summary>
        public static Texture2D Grating()
        {
            if (_grating != null)
                return _grating;
            const int n = 256;
            const int pitch = 16;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var gx = x % pitch;
                var gy = y % (pitch * 2);
                var bar = gx < 3 || gy < 2;
                var k = bar ? 0.7f + Hash(x, y, 81) * 0.06f - (gx == 2 || gy == 1 ? 0.15f : 0f) : 0.12f + Noise(x / 32f, y / 32f, 82, 8, 8) * 0.06f;
                // Main bearers every 128 px.
                if (x % 128 < 6)
                    k = 0.8f - (x % 128) * 0.03f;
                px[y * n + x] = Grey(k);
            }

            _grating = StationSurfaces.Finish(px, n, "SU_WorkshopGrating");
            return _grating;
        }

        /// <summary>Painted metal, worn: an even coat with soft mottling, chips and scuffs showing bare metal.</summary>
        public static Texture2D WornPaint()
        {
            if (_paint != null)
                return _paint;
            const int n = 256;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var k = 0.86f + Noise(u * 5f, v * 5f, 91, 5, 5) * 0.08f + Hash(x, y, 92) * 0.02f;
                // Chips: where a fine noise peaks, the paint is gone (lighter bare steel with a dark rim).
                var chip = Noise(u * 22f, v * 22f, 93, 22, 22) * 0.7f + Noise(u * 60f, v * 60f, 94, 60, 60) * 0.3f;
                if (chip > 0.8f)
                    k = 1f;
                else if (chip > 0.77f)
                    k *= 0.6f;
                // Vertical scuffs.
                k -= Mathf.Max(0f, Noise(u * 40f, v * 2f, 95, 40, 2) - 0.75f) * 0.6f;
                px[y * n + x] = Grey(k);
            }

            _paint = StationSurfaces.Finish(px, n, "SU_WorkshopPaint");
            return _paint;
        }

        /// <summary>Yellow and black hazard stripes at 45°, scuffed (coloured: used with a white tint).</summary>
        public static Texture2D Hazard()
        {
            if (_hazard != null)
                return _hazard;
            const int n = 128;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var stripe = ((x + y) / 32) % 2 == 0;
                var wear = Noise(x / 16f, y / 16f, 101, 8, 8) * 0.18f + Hash(x, y, 102) * 0.05f;
                var c = stripe ? new Color(0.98f, 0.74f, 0.1f) : new Color(0.08f, 0.08f, 0.09f);
                c = Color.Lerp(c, new Color(0.45f, 0.43f, 0.4f), wear);
                px[y * n + x] = c;
            }

            _hazard = StationSurfaces.Finish(px, n, "SU_WorkshopHazard");
            return _hazard;
        }

        /// <summary>Clean-room floor: four large tiles per texture with thin pale grout, a faint sheen per tile.</summary>
        public static Texture2D LabTile()
        {
            if (_labTile != null)
                return _labTile;
            const int n = 256;
            const int tile = 128;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var tx = x / tile;
                var ty = y / tile;
                var k = 0.84f + (Hash(tx, ty, 111) - 0.5f) * 0.05f + Noise(u * 8f, v * 8f, 112, 8, 8) * 0.04f +
                        Hash(x, y, 113) * 0.015f;
                // Sheen: a soft diagonal gradient across each tile, as polished resin catches the ceiling light.
                var lx = (x % tile) / (float)tile;
                var ly = (y % tile) / (float)tile;
                k += (lx + ly - 1f) * 0.025f;
                var gx = x % tile;
                var gy = y % tile;
                if (gx < 2 || gy < 2)
                    k = 0.7f;
                else if (gx == 2 || gy == 2)
                    k *= 1.04f;
                px[y * n + x] = Grey(k);
            }

            _labTile = StationSurfaces.Finish(px, n, "SU_LabTile");
            return _labTile;
        }

        /// <summary>White wall cladding: two tall panels per texture, a dark shadow gap round each, a faint grain.</summary>
        public static Texture2D LabPanel()
        {
            if (_labPanel != null)
                return _labPanel;
            const int n = 256;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var k = 0.9f + Noise(u * 3f, v * 6f, 121, 3, 6) * 0.04f + Hash(x, y, 122) * 0.012f;
                var px0 = x % 128;
                // Shadow gap between panels, a lit lip beside it; a horizontal gap at 2/3 height.
                if (px0 < 3)
                    k = 0.42f + px0 * 0.08f;
                else if (px0 == 3)
                    k *= 1.05f;
                var gy = Mathf.Abs(y - n * 2 / 3);
                if (gy < 2)
                    k = 0.5f;
                else if (gy == 2)
                    k *= 1.05f;
                px[y * n + x] = Grey(k);
            }

            _labPanel = StationSurfaces.Finish(px, n, "SU_LabPanel");
            return _labPanel;
        }

        /// <summary>Acoustic ceiling tiles: a square grid of tiles in a thin T-bar frame, each finely perforated.</summary>
        public static Texture2D AcousticCeiling()
        {
            if (_acoustic != null)
                return _acoustic;
            const int n = 256;
            const int tile = 128;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var gx = x % tile;
                var gy = y % tile;
                var k = 0.88f + Hash(x, y, 131) * 0.03f;
                // Perforations on a 8 px grid, inset from the tile's edge.
                if (gx > 10 && gx < tile - 10 && gy > 10 && gy < tile - 10 && gx % 8 < 2 && gy % 8 < 2)
                    k = 0.48f;
                // T-bar frame.
                if (gx < 4 || gy < 4)
                    k = gx < 2 || gy < 2 ? 0.62f : 0.95f;
                px[y * n + x] = Grey(k);
            }

            _acoustic = StationSurfaces.Finish(px, n, "SU_LabAcoustic");
            return _acoustic;
        }
    }
}
