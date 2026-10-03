using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Core.Utils
{
    /// <summary>
    /// A small QR Code encoder (ISO/IEC 18004): byte mode, error correction level M, versions 1–9 — enough for
    /// a URL. Follows the reference algorithm (Reed–Solomon over GF(256), block interleaving, the eight masks
    /// scored by the standard penalty rules). No dependency; the result is a module grid or a point-filtered
    /// texture with its quiet zone.
    /// </summary>
    public sealed class QrCode
    {
        static readonly int[] EccPerBlockM = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] BlocksM = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
        const int FormatBitsM = 0;

        public readonly int Version;
        public readonly int Size;
        readonly bool[,] _modules;
        readonly bool[,] _function;

        public bool this[int x, int y] => _modules[y, x];

        QrCode(int version)
        {
            Version = version;
            Size = version * 4 + 17;
            _modules = new bool[Size, Size];
            _function = new bool[Size, Size];
        }

        /// <summary>Encode <paramref name="text"/> (UTF-8 bytes) in the smallest version that holds it.</summary>
        public static QrCode Encode(string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var version = 1;
            // Versions 1-9 (the byte count is 8 bits long up to version 9).
            for (; version <= 9; version++)
                if (data.Length * 8 + 4 + 8 <= DataCodewords(version) * 8)
                    break;
            if (version > 9)
                throw new ArgumentException("Text too long for a version 1-9 QR code");

            // Bit stream: byte mode, 8-bit count, data, terminator, byte padding, pad codewords.
            var bits = new List<bool>();
            Append(bits, 0x4, 4);
            Append(bits, data.Length, 8);
            foreach (var b in data)
                Append(bits, b, 8);
            var capacity = DataCodewords(version) * 8;
            Append(bits, 0, Math.Min(4, capacity - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
                Append(bits, pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (var i = 0; i < bits.Count; i++)
                if (bits[i])
                    codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            var qr = new QrCode(version);
            qr.DrawFunctionPatterns();
            qr.DrawCodewords(qr.AddEccAndInterleave(codewords));

            // The mask with the lowest penalty.
            var best = 0;
            var bestPenalty = int.MaxValue;
            for (var m = 0; m < 8; m++)
            {
                qr.ApplyMask(m);
                qr.DrawFormatBits(m);
                var p = qr.Penalty();
                if (p < bestPenalty)
                {
                    best = m;
                    bestPenalty = p;
                }

                qr.ApplyMask(m);
            }

            qr.ApplyMask(best);
            qr.DrawFormatBits(best);
            return qr;
        }

        /// <summary>The code as a texture: dark modules on white, a 4-module quiet zone, point filtered.</summary>
        public Texture2D ToTexture(Color dark, Color light, int pixelsPerModule = 8)
        {
            const int quiet = 4;
            var n = (Size + quiet * 2) * pixelsPerModule;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SU_QR" };
            var px = new Color32[n * n];
            Color32 d = dark, l = light;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var mx = x / pixelsPerModule - quiet;
                // Texture rows run bottom-up; the code's row 0 is its top.
                var my = Size - 1 - (y / pixelsPerModule - quiet);
                var on = mx >= 0 && my >= 0 && mx < Size && my < Size && _modules[my, mx];
                px[y * n + x] = on ? d : l;
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ── Capacity ───────────────────────────────────────────────────────────

        static int RawDataModules(int ver)
        {
            var result = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                var numAlign = ver / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (ver >= 7)
                    result -= 36;
            }

            return result;
        }

        static int DataCodewords(int ver) => RawDataModules(ver) / 8 - EccPerBlockM[ver] * BlocksM[ver];

        static void Append(List<bool> bits, int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
                bits.Add(((value >> i) & 1) != 0);
        }

        // ── Function patterns ──────────────────────────────────────────────────

        void Set(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _function[y, x] = true;
        }

        void DrawFunctionPatterns()
        {
            for (var i = 0; i < Size; i++)
            {
                Set(6, i, i % 2 == 0);
                Set(i, 6, i % 2 == 0);
            }

            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            var align = AlignmentPositions();
            var n = align.Length;
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                if (i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0)
                    continue;
                DrawAlignment(align[i], align[j]);
            }

            DrawFormatBits(0);
            DrawVersion();
        }

        void DrawFinder(int cx, int cy)
        {
            for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < Size && y >= 0 && y < Size)
                    Set(x, y, dist != 2 && dist != 4);
            }
        }

        void DrawAlignment(int cx, int cy)
        {
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                Set(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        int[] AlignmentPositions()
        {
            if (Version == 1)
                return Array.Empty<int>();
            var numAlign = Version / 7 + 2;
            var step = (Version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
            var result = new int[numAlign];
            result[0] = 6;
            for (int i = result.Length - 1, pos = Size - 7; i >= 1; i--, pos -= step)
                result[i] = pos;
            return result;
        }

        void DrawFormatBits(int mask)
        {
            var data = FormatBitsM << 3 | mask;
            var rem = data;
            for (var i = 0; i < 10; i++)
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            var bits = (data << 10 | rem) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;

            for (var i = 0; i <= 5; i++)
                Set(8, i, Bit(i));
            Set(8, 7, Bit(6));
            Set(8, 8, Bit(7));
            Set(7, 8, Bit(8));
            for (var i = 9; i < 15; i++)
                Set(14 - i, 8, Bit(i));

            for (var i = 0; i < 8; i++)
                Set(Size - 1 - i, 8, Bit(i));
            for (var i = 8; i < 15; i++)
                Set(8, Size - 15 + i, Bit(i));
            Set(8, Size - 8, true); // the dark module
        }

        void DrawVersion()
        {
            if (Version < 7)
                return;
            var rem = Version;
            for (var i = 0; i < 12; i++)
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = Version << 12 | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                int a = Size - 11 + i % 3, b = i / 3;
                Set(a, b, bit);
                Set(b, a, bit);
            }
        }

        // ── Error correction ───────────────────────────────────────────────────

        byte[] AddEccAndInterleave(byte[] data)
        {
            var numBlocks = BlocksM[Version];
            var blockEccLen = EccPerBlockM[Version];
            var rawCodewords = RawDataModules(Version) / 8;
            var numShortBlocks = numBlocks - rawCodewords % numBlocks;
            var shortBlockLen = rawCodewords / numBlocks;

            var blocks = new List<byte[]>();
            var divisor = RsDivisor(blockEccLen);
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                var datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                var dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                var ecc = RsRemainder(dat, divisor);
                var block = new byte[shortBlockLen + 1];
                Array.Copy(dat, block, datLen);
                // Short blocks keep a hole at the end of their data (skipped when interleaving).
                Array.Copy(ecc, 0, block, shortBlockLen + 1 - blockEccLen, blockEccLen);
                blocks.Add(block);
            }

            var result = new List<byte>(rawCodewords);
            for (var i = 0; i < blocks[0].Length; i++)
            for (var j = 0; j < blocks.Count; j++)
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    result.Add(blocks[j][i]);
            return result.ToArray();
        }

        static byte[] RsDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            var root = 1;
            for (var i = 0; i < degree; i++)
            {
                for (var j = 0; j < result.Length; j++)
                {
                    result[j] = RsMultiply(result[j], root);
                    if (j + 1 < result.Length)
                        result[j] ^= result[j + 1];
                }

                root = RsMultiply(root, 0x02);
            }

            return result;
        }

        static byte[] RsRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (var b in data)
            {
                var factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (var i = 0; i < result.Length; i++)
                    result[i] ^= RsMultiply(divisor[i], factor);
            }

            return result;
        }

        static byte RsMultiply(int x, int y)
        {
            var z = 0;
            for (var i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }

            return (byte)z;
        }

        // ── Data placement and masks ───────────────────────────────────────────

        void DrawCodewords(byte[] data)
        {
            var i = 0;
            for (var right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                    right = 5;
                for (var vert = 0; vert < Size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vert : vert;
                    if (_function[y, x] || i >= data.Length * 8)
                        continue;
                    _modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                    i++;
                }
            }
        }

        void ApplyMask(int mask)
        {
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                };
                if (invert && !_function[y, x])
                    _modules[y, x] = !_modules[y, x];
            }
        }

        /// <summary>The standard penalty: runs, 2×2 blocks, finder-like patterns, dark/light balance.</summary>
        int Penalty()
        {
            var result = 0;
            for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < Size; a++)
            {
                var run = 0;
                var last = false;
                var line = new bool[Size];
                for (var b = 0; b < Size; b++)
                {
                    var m = pass == 0 ? _modules[a, b] : _modules[b, a];
                    line[b] = m;
                    if (b > 0 && m == last)
                    {
                        run++;
                        if (run == 5)
                            result += 3;
                        else if (run > 5)
                            result++;
                    }
                    else
                        run = 1;

                    last = m;
                }

                // 1:1:3:1:1 finder-like runs with 4 light modules on a side.
                for (var b = 0; b + 10 < Size + 4; b++)
                {
                    bool At(int k) => k >= 0 && k < Size && line[k];
                    var core = At(b) && !At(b + 1) && At(b + 2) && At(b + 3) && At(b + 4) && !At(b + 5) && At(b + 6);
                    if (!core)
                        continue;
                    var before = !At(b - 1) && !At(b - 2) && !At(b - 3) && !At(b - 4);
                    var after = !At(b + 7) && !At(b + 8) && !At(b + 9) && !At(b + 10);
                    if (before || after)
                        result += 40;
                }
            }

            for (var y = 0; y < Size - 1; y++)
            for (var x = 0; x < Size - 1; x++)
            {
                var c = _modules[y, x];
                if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1])
                    result += 3;
            }

            var dark = 0;
            foreach (var m in _modules)
                if (m)
                    dark++;
            var total = Size * Size;
            var k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            result += Math.Max(0, k2) * 10;
            return result;
        }
    }
}
