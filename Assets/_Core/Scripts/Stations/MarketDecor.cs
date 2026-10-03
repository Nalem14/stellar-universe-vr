using Core.UI;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// Set dressing of the galactic exchange (marketplace), procedural and on shared materials: a long trading
    /// floor (deck, coffered ceiling with light lines), a tall bay on space at the far end, two ticker boards
    /// scrolling quotes along the side walls (one material, its offset animated), stacked guild freight
    /// containers in the far corners (banded in the three resource colours), and the exchange pit in the middle
    /// — a sunken ring around an emitter plinth over which the offers float as crates. Two operator desks at the
    /// stand. Static geometry is merged per material (<see cref="MeshBatch"/>); colliders are plain boxes.
    /// </summary>
    public static class MarketDecor
    {
        public const float HalfWidth = 6f;
        public const float Depth = 10.6f;
        public const float Height = 5.6f;
        public const float BackZ = -2.2f;
        const float SillTop = 1.2f;
        const float WindowTop = 4.8f;
        /// <summary>The exchange pit's centre (room local, floor): the offers float above it.</summary>
        public static readonly Vector3 Pit = new(0f, 0f, 5.4f);

        public static readonly Color Mineral = new(1f, 0.6f, 0.28f, 1f);
        public static readonly Color Crystal = new(0.72f, 0.55f, 1f, 1f);
        public static readonly Color Biomass = new(0.42f, 1f, 0.5f, 1f);
        public static readonly Color Module = new(0.36f, 0.9f, 1f, 1f);

        public static Color CategoryColor(string category, string itemKey) => category == "module"
            ? Module
            : itemKey switch
            {
                "crystal" => Crystal,
                "biomass" => Biomass,
                _ => Mineral
            };

        public sealed class Refs
        {
            public Transform BrowseMount;
            public Transform CounterMount;
            /// <summary>The scrolling quote boards (offset driven by the room).</summary>
            public Material Ticker;
        }

        public static Refs Build(Transform room, CicArtKit art, Color accent)
        {
            var refs = new Refs();
            var metal = art.MetalPanel(0.55f);
            var dark = art.DarkPanel(0.3f);
            var deck = art.DeckMat(0.62f);
            var glow = art.Lit(Texture2D.whiteTexture, accent, 2.6f);
            var soft = art.Lit(Texture2D.whiteTexture, accent, 1.4f);
            var cyan = art.CyanEmit(1.8f);
            var amber = art.AmberEmit(2f);
            var mineral = art.Lit(Texture2D.whiteTexture, Mineral, 1.8f);
            var crystal = art.Lit(Texture2D.whiteTexture, Crystal, 1.8f);
            var biomass = art.Lit(Texture2D.whiteTexture, Biomass, 1.8f);
            var cyanFrame = art.CyanEmit(3f);
            var b = new MeshBatch();

            var midZ = (BackZ + Depth) * 0.5f;
            var len = Depth - BackZ;

            // ── Shell ─────────────────────────────────────────────────────────────
            b.Box(new Vector3(0f, -0.05f, midZ), new Vector3(HalfWidth * 2f + 0.4f, 0.1f, len + 0.4f), deck);
            b.Box(new Vector3(0f, Height + 0.06f, midZ), new Vector3(HalfWidth * 2f + 0.4f, 0.12f, len + 0.4f), dark);
            b.Box(new Vector3(0f, Height * 0.5f, BackZ - 0.1f), new Vector3(HalfWidth * 2f + 0.4f, Height, 0.2f), dark);
            for (var s = -1; s <= 1; s += 2)
                b.Box(new Vector3(s * (HalfWidth + 0.1f), Height * 0.5f, midZ), new Vector3(0.2f, Height, len + 0.4f), dark);

            // Far end: sill, head, mullions — open on space (the room stands over our ship, the shared exterior around it).
            b.Box(new Vector3(0f, SillTop * 0.5f, Depth + 0.1f), new Vector3(HalfWidth * 2f + 0.4f, SillTop, 0.2f), dark);
            b.Box(new Vector3(0f, (WindowTop + Height) * 0.5f, Depth + 0.1f), new Vector3(HalfWidth * 2f + 0.4f, Height - WindowTop, 0.2f), dark);
            b.Box(new Vector3(0f, SillTop, Depth - 0.12f), new Vector3(HalfWidth * 2f, 0.06f, 0.34f), metal);
            b.Box(new Vector3(0f, SillTop - 0.04f, Depth - 0.3f), new Vector3(HalfWidth * 2f, 0.015f, 0.01f), cyan);
            for (var i = -2; i <= 2; i++)
            {
                var x = i * 2.6f;
                b.Box(new Vector3(x, (SillTop + WindowTop) * 0.5f, Depth + 0.02f), new Vector3(0.16f, WindowTop - SillTop, 0.2f), metal);
                b.Box(new Vector3(x, (SillTop + WindowTop) * 0.5f, Depth - 0.09f), new Vector3(0.02f, WindowTop - SillTop - 0.3f, 0.01f), soft);
            }

            for (var s = -1; s <= 1; s += 2)
                b.Box(new Vector3(s * (HalfWidth - 0.45f), (SillTop + WindowTop) * 0.5f, Depth + 0.02f),
                    new Vector3(0.9f, WindowTop - SillTop, 0.2f), dark);
            b.Box(new Vector3(0f, WindowTop + 0.05f, Depth - 0.02f), new Vector3(HalfWidth * 2f, 0.1f, 0.24f), metal);

            // Side ribs every 2 m with a lit slit, a cornice and a kick line in the accent.
            for (var z = 0.4f; z < Depth - 0.3f; z += 2f)
                for (var s = -1; s <= 1; s += 2)
                {
                    b.Box(new Vector3(s * (HalfWidth - 0.08f), Height * 0.5f, z), new Vector3(0.18f, Height, 0.26f), metal);
                    b.Box(new Vector3(s * (HalfWidth - 0.175f), Height * 0.45f, z), new Vector3(0.01f, Height * 0.6f, 0.04f), glow);
                }

            for (var s = -1; s <= 1; s += 2)
            {
                b.Box(new Vector3(s * (HalfWidth - 0.14f), Height - 0.22f, midZ), new Vector3(0.28f, 0.16f, len), metal);
                b.Box(new Vector3(s * (HalfWidth - 0.02f), 0.08f, midZ), new Vector3(0.04f, 0.03f, len), glow);
            }

            // Ceiling: three coffer lines and cross beams.
            for (var i = -1; i <= 1; i++)
                b.Box(new Vector3(i * 2.6f, Height - 0.02f, midZ), new Vector3(0.08f, 0.02f, len - 0.6f), i == 0 ? glow : soft);
            for (var z = 0.4f; z < Depth; z += 2f)
                b.Box(new Vector3(0f, Height - 0.1f, z), new Vector3(HalfWidth * 2f, 0.16f, 0.2f), metal);

            // ── Ticker boards on both side walls (frame + lit quad) ─────────────
            // Texture carries the light (no flat emission on top, it would wash the dark screen out).
            refs.Ticker = new Material(art.Lit(TickerTexture(accent), new Color(1.6f, 1.6f, 1.6f, 1f), 0f)) { name = "SU_MarketTicker" };
            for (var s = -1; s <= 1; s += 2)
            {
                var x = s * (HalfWidth - 0.21f);
                b.Box(new Vector3(x, 2.9f, 5f), new Vector3(0.06f, 2.9f, 7.2f), dark);
                b.Box(new Vector3(x - s * 0.04f, 4.38f, 5f), new Vector3(0.03f, 0.03f, 7.2f), cyanFrame);
                b.Box(new Vector3(x - s * 0.04f, 1.42f, 5f), new Vector3(0.03f, 0.03f, 7.2f), cyanFrame);
                var board = GateRoomDecor.Quad(room, "TickerBoard", new Vector3(x - s * 0.035f, 2.9f, 5f), new Vector3(7f, 2.8f, 1f), refs.Ticker);
                board.transform.localRotation = Quaternion.Euler(0f, s * 90f, 0f);
            }

            // ── Guild freight: containers stacked in the far corners ─────────────
            var bands = new[] { mineral, crystal, biomass };
            var k = 0;
            for (var s = -1; s <= 1; s += 2)
                for (var row = 0; row < 2; row++)
                    for (var level = 0; level < 2 - row % 2; level++)
                    {
                        var c = new Vector3(s * (HalfWidth - 1.05f), 0.55f + level * 1.1f, Depth - 1.1f - row * 1.4f);
                        Container(b, c, metal, dark, bands[k++ % 3]);
                    }

            // ── Exchange pit: sunken ring step, rail, emitter plinth ──────────────
            var ring = new LatheMesh(Pit);
            ring.Revolve(new[] { new Vector2(2.7f, 0f), new Vector2(2.7f, 0.14f), new Vector2(2.55f, 0.14f), new Vector2(2.55f, 0.02f) }, 0f, 360f, false);
            LatheMesh.Part(room, "PitStep", ring.ToMesh("SU_MarketPitStep"), metal);
            var ringGlow = new LatheMesh(Pit);
            ringGlow.Revolve(new[] { new Vector2(2.56f, 0.145f), new Vector2(2.7f, 0.145f) }, 0f, 360f, false);
            ringGlow.Revolve(new[] { new Vector2(1.9f, 0.006f), new Vector2(1.96f, 0.006f) }, 0f, 360f, false);
            LatheMesh.Part(room, "PitGlow", ringGlow.ToMesh("SU_MarketPitGlow"), glow);
            var plinth = new LatheMesh(Pit);
            plinth.Revolve(new[]
            {
                new Vector2(0.95f, 0f), new Vector2(0.95f, 0.12f), new Vector2(0.7f, 0.2f), new Vector2(0.55f, 0.78f),
                new Vector2(0.72f, 0.86f), new Vector2(0.72f, 0.92f), new Vector2(0.4f, 0.95f)
            }, 0f, 360f, false);
            LatheMesh.Part(room, "Plinth", plinth.ToMesh("SU_MarketPlinth"), metal);
            var emitter = new LatheMesh(Pit);
            emitter.Revolve(new[] { new Vector2(0.4f, 0.955f), new Vector2(0.66f, 0.925f) }, 0f, 360f, false);
            emitter.Revolve(new[] { new Vector2(0.56f, 0.5f), new Vector2(0.6f, 0.6f) }, 0f, 360f, false);
            LatheMesh.Part(room, "Emitter", emitter.ToMesh("SU_MarketEmitter"), glow);
            for (var i = 0; i < 8; i++)
            {
                var d = LatheMesh.Dir(i * 45f + 22.5f);
                b.Box(Pit + d * 2.3f + new Vector3(0f, 0.004f, 0f), new Vector3(0.05f, 0.008f, 0.5f), soft, Quaternion.LookRotation(d));
            }

            // Floor runners from the stand to the pit.
            for (var s = -1; s <= 1; s += 2)
                b.Box(new Vector3(s * 0.6f, 0.004f, 1.6f), new Vector3(0.03f, 0.008f, 3.2f), glow);
            b.Build(room, "MarketShell");

            // ── Operator desks at the stand: the exchange (left), the counter (right) ─
            refs.BrowseMount = GateRoomDecor.Desk(room, "ExchangeDesk", new Vector3(-1.15f, 0f, 0.8f),
                GateRoomDecor.FaceStand(-1.15f, 0.8f), 1.25f, metal, dark, glow, amber);
            refs.CounterMount = GateRoomDecor.Desk(room, "CounterDesk", new Vector3(1.15f, 0f, 0.8f),
                GateRoomDecor.FaceStand(1.15f, 0.8f), 1.25f, metal, dark, glow, amber);

            // ── Colliders: the body stays on the floor and away from the pit ──────
            Solid(room, new Vector3(0f, -0.25f, midZ), new Vector3(HalfWidth * 2f, 0.5f, len));
            Solid(room, new Vector3(0f, 2.5f, BackZ - 0.3f), new Vector3(HalfWidth * 2f, 5f, 0.6f));
            Solid(room, new Vector3(0f, 2.5f, Depth + 0.3f), new Vector3(HalfWidth * 2f, 5f, 0.6f));
            for (var s = -1; s <= 1; s += 2)
                Solid(room, new Vector3(s * (HalfWidth + 0.3f), 2.5f, midZ), new Vector3(0.6f, 5f, len));
            Solid(room, Pit + new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.6f));
            return refs;
        }

        /// <summary>A guild container: ribbed body, end doors, a lit band in its cargo's colour.</summary>
        static void Container(MeshBatch b, Vector3 c, Material metal, Material dark, Material band)
        {
            var size = new Vector3(1.9f, 1.05f, 1.2f);
            b.Box(c, size, dark);
            for (var i = -3; i <= 3; i++)
                b.Box(c + new Vector3(i * 0.27f, 0f, 0f), new Vector3(0.05f, size.y + 0.02f, size.z + 0.04f), metal);
            b.Box(c + new Vector3(0f, size.y * 0.5f - 0.02f, 0f), new Vector3(size.x + 0.04f, 0.04f, size.z + 0.06f), metal);
            b.Box(c + new Vector3(0f, -size.y * 0.5f + 0.02f, 0f), new Vector3(size.x + 0.04f, 0.04f, size.z + 0.06f), metal);
            b.Box(c + new Vector3(0f, 0.18f, -size.z * 0.5f - 0.03f), new Vector3(size.x * 0.9f, 0.06f, 0.01f), band);
        }

        static void Solid(Transform room, Vector3 c, Vector3 size)
        {
            var go = new GameObject("MarketCollider");
            go.transform.SetParent(room, false);
            go.transform.localPosition = c;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// The quote board: rows of glyph blocks (tickers, prices, up / down arrows in green / red) on a dark
        /// screen, generated once; wraps vertically so the room scrolls it by offset.
        /// </summary>
        static Texture2D TickerTexture(Color accent)
        {
            const int w = 512, h = 256, row = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "SU_MarketTicker", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color32[w * h];
            var bg = new Color(0.015f, 0.04f, 0.045f, 1f);
            for (var i = 0; i < px.Length; i++)
                px[i] = bg;
            var seed = 977;
            int Rand(int n)
            {
                seed = seed * 1103515245 + 12345 & 0x7fffffff;
                return seed % n;
            }

            var up = new Color(0.35f, 1f, 0.55f, 1f);
            var down = new Color(1f, 0.35f, 0.3f, 1f);
            var name = Color.Lerp(accent, Color.white, 0.35f);
            var dim = new Color(0.35f, 0.6f, 0.65f, 1f);
            void Block(int x0, int y0, int bw, int bh, Color c)
            {
                for (var y = y0; y < y0 + bh && y < h; y++)
                    for (var x = x0; x < x0 + bw && x < w; x++)
                        px[y * w + x] = c;
            }

            for (var r = 0; r < h / row; r++)
            {
                var y = r * row + 4;
                // Separator line between rows.
                Block(0, r * row, w, 1, new Color(0.06f, 0.16f, 0.18f, 1f));
                for (var col = 0; col < 2; col++)
                {
                    var x = 8 + col * 256;
                    // Ticker name: 3–5 glyphs.
                    var glyphs = 3 + Rand(3);
                    for (var g = 0; g < glyphs; g++)
                        Block(x + g * 9, y, 6 + Rand(2), 8, name);
                    x += 64;
                    // Price: digits with a decimal point.
                    var digits = 4 + Rand(3);
                    for (var g = 0; g < digits; g++)
                    {
                        Block(x, y + Rand(2), 5, 7, dim * 1.6f);
                        x += 8;
                        if (g == digits - 3)
                        {
                            Block(x, y, 2, 2, dim * 1.6f);
                            x += 4;
                        }
                    }

                    // Change: arrow and a bar.
                    var rising = Rand(3) != 0;
                    var c = rising ? up : down;
                    x = 8 + col * 256 + 150;
                    for (var t = 0; t < 4; t++)
                        Block(x + t, rising ? y + t : y + 7 - t - 1, 7 - t * 2, 1, c);
                    Block(x + 12, y + 2, 10 + Rand(70), 4, c * 0.8f);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
