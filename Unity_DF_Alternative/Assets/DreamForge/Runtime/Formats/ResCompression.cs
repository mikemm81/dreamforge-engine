// SPDX-License-Identifier: GPL-3.0-only
// RES compression methods. Written from the "Compression" section of docs/formats/resources.md only.
using System;

namespace DreamForge.Formats
{
    public static class ResCompression
    {
        public const int None = 0, Rle = 1, Lzw = 2, Lzss = 3;

        public readonly struct Result
        {
            public readonly bool Ok;
            public readonly int Produced;
            public readonly string Message;

            public Result(bool ok, int produced, string message)
            {
                Ok = ok;
                Produced = produced;
                Message = message;
            }
        }

        /// <summary>
        /// Decodes <paramref name="src"/> into exactly <paramref name="expected"/> bytes.
        /// Never throws on bad data: if the input runs out, the bytes produced so far are returned
        /// with Ok = false.
        /// </summary>
        public static Result Decode(byte[] src, int method, int expected, out byte[] output)
        {
            output = new byte[Math.Max(0, expected)];
            switch (method)
            {
                case None:
                {
                    int n = Math.Min(src.Length, output.Length);
                    Buffer.BlockCopy(src, 0, output, 0, n);
                    return Finish(n, expected);
                }
                case Rle: return Finish(DecodeRle(src, output), expected);
                case Lzss: return Finish(DecodeLzss(src, output), expected);
                case Lzw: return new Result(false, 0, "LZW (code 2) is defined but never used; not supported");
                default: return new Result(false, 0, $"unknown compression code {method}");
            }
        }

        static Result Finish(int produced, int expected) =>
            produced >= expected
                ? new Result(true, produced, null)
                : new Result(false, produced, $"data runs out after {produced} of {expected} bytes");

        // Signed control byte c: c < 0 repeats the next byte 1 - c times, otherwise copies the next c + 1 bytes.
        static int DecodeRle(byte[] src, byte[] dst)
        {
            int s = 0, d = 0;
            while (d < dst.Length && s < src.Length)
            {
                int c = (sbyte)src[s++];
                if (c < 0)
                {
                    if (s >= src.Length) break;
                    byte b = src[s++];
                    for (int k = 1 - c; k > 0 && d < dst.Length; k--)
                        dst[d++] = b;
                }
                else
                {
                    for (int k = c + 1; k > 0 && d < dst.Length; k--)
                    {
                        if (s >= src.Length) return d;
                        dst[d++] = src[s++];
                    }
                }
            }
            return d;
        }

        // 4,096-byte ring filled with 0xFE, writing from 0xFEE. Flag bits from least significant up:
        // 1 = literal byte, 0 = (b0, b1) -> position b0 | (b1 & 0xF0) << 4, length (b1 & 0x0F) + 3.
        static int DecodeLzss(byte[] src, byte[] dst)
        {
            var ring = new byte[4096];
            for (int i = 0; i < ring.Length; i++) ring[i] = 0xFE;
            int r = 0xFEE, s = 0, d = 0;
            int flags = 0, bitsLeft = 0;

            while (d < dst.Length)
            {
                if (bitsLeft == 0)
                {
                    if (s >= src.Length) break;
                    flags = src[s++];
                    bitsLeft = 8;
                }
                bool literal = (flags & 1) != 0;
                flags >>= 1;
                bitsLeft--;

                if (literal)
                {
                    if (s >= src.Length) break;
                    byte b = src[s++];
                    dst[d++] = b;
                    ring[r] = b;
                    r = (r + 1) & 0xFFF;
                }
                else
                {
                    if (s + 1 >= src.Length) break;
                    int b0 = src[s++], b1 = src[s++];
                    int pos = b0 | (b1 & 0xF0) << 4;
                    int len = (b1 & 0x0F) + 3;
                    for (int k = 0; k < len && d < dst.Length; k++)
                    {
                        byte b = ring[(pos + k) & 0xFFF];
                        dst[d++] = b;
                        ring[r] = b;
                        r = (r + 1) & 0xFFF;
                    }
                }
            }
            return d;
        }
    }
}
