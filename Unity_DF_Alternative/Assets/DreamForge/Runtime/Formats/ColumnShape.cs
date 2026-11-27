// SPDX-License-Identifier: GPL-3.0-only
// Column shapes (EH type 6 in M and P): docs/formats/images.md, "Column shapes".
using System;
using System.IO;

namespace DreamForge.Formats
{
    /// <summary>
    /// A decoded column shape on its canvas: palette indices plus a mask (undrawn pixels are transparent),
    /// top row first. The canvas is the stated canvas width (or wider, if columns are stored beyond it) by
    /// 192 rows (or taller, if runs go below row 192).
    /// </summary>
    public sealed class ColumnShape
    {
        public const int CanvasHeight = 192;   // M, P: the same row space as the walls

        public int CanvasWidth { get; private set; }
        public int FirstColumn { get; private set; }
        public int LastColumn { get; private set; }
        public int FirstRow { get; private set; }
        public int LastRow { get; private set; }

        public int Width { get; private set; }
        public int Height { get; private set; }
        /// <summary>Width x Height palette indices, top row first; meaningless where Opaque is false.</summary>
        public byte[] Indices { get; private set; }
        public bool[] Opaque { get; private set; }
        public int DrawnPixels { get; private set; }

        /// <summary>Documented: shapes of 16 bytes or less are empty placeholders.</summary>
        public static bool IsStub(byte[] data) => data.Length <= 16;

        /// <summary>Decodes a shape's unpacked data. Throws InvalidDataException if a run points outside it.</summary>
        public static ColumnShape Decode(byte[] d)
        {
            if (d.Length < 10) throw new InvalidDataException($"shape is {d.Length} bytes; the header is 10");
            var s = new ColumnShape
            {
                CanvasWidth = U16(d, 0), FirstColumn = U16(d, 2), LastColumn = U16(d, 4),
                FirstRow = U16(d, 6), LastRow = U16(d, 8),
            };
            int columns = s.LastColumn - s.FirstColumn + 1;
            if (columns < 0 || 0x0A + 2 * columns > d.Length)
                throw new InvalidDataException($"column table for columns {s.FirstColumn}-{s.LastColumn} does not fit in {d.Length} bytes");

            // First pass: find the extent, so the canvas can grow if runs go past 192 rows.
            int maxRow = -1;
            ForEachRun(d, columns, (col, start, end, src) => { if (end > start) maxRow = Math.Max(maxRow, end - 1); });

            s.Width = Math.Max(s.CanvasWidth, s.LastColumn + 1);
            s.Height = Math.Max(CanvasHeight, maxRow + 1);
            s.Indices = new byte[s.Width * s.Height];
            s.Opaque = new bool[s.Width * s.Height];
            int drawn = 0;
            ForEachRun(d, columns, (col, start, end, src) =>
            {
                int x = s.FirstColumn + col;
                for (int y = start; y < end; y++)
                {
                    int p = src + y;   // documented: the pixel is the byte at source + row
                    if (p < 0 || p >= d.Length)
                        throw new InvalidDataException($"column {x} row {y}: pixel at {p} is outside the {d.Length}-byte shape");
                    int i = y * s.Width + x;
                    if (!s.Opaque[i]) drawn++;
                    s.Indices[i] = d[p];
                    s.Opaque[i] = true;
                }
            });
            s.DrawnPixels = drawn;
            return s;
        }

        /// <summary>
        /// Documented run list: u16 end x 2, s16 source, u16 start x 2, repeated; ends when the first value is 0.
        /// Rows start to end - 1 are drawn.
        /// </summary>
        static void ForEachRun(byte[] d, int columns, Action<int, int, int, int> run)
        {
            for (int k = 0; k < columns; k++)
            {
                int p = U16(d, 0x0A + 2 * k);
                while (true)
                {
                    if (p + 2 > d.Length) throw new InvalidDataException($"column {k}: run list runs past the end");
                    int end2 = U16(d, p);
                    if (end2 == 0) break;
                    if (p + 6 > d.Length) throw new InvalidDataException($"column {k}: run at {p} runs past the end");
                    int src = (short)U16(d, p + 2);
                    int start2 = U16(d, p + 4);
                    p += 6;
                    run(k, start2 / 2, end2 / 2, src);
                }
            }
        }

        /// <summary>RGBA, top row first; transparent pixels are 0, 0, 0, 0.</summary>
        public byte[] ToRgba(Palette palette)
        {
            var o = new byte[Indices.Length * 4];
            for (int i = 0; i < Indices.Length; i++)
            {
                if (!Opaque[i]) continue;
                int c = Indices[i] * 3;
                o[i * 4] = palette.Rgb[c];
                o[i * 4 + 1] = palette.Rgb[c + 1];
                o[i * 4 + 2] = palette.Rgb[c + 2];
                o[i * 4 + 3] = 255;
            }
            return o;
        }

        /// <summary>Per pixel: (1, index) if drawn, (0, 0) if not. For hashing against tools/res_shapes.py.</summary>
        public byte[] MaskAndIndex()
        {
            var o = new byte[Indices.Length * 2];
            for (int i = 0; i < Indices.Length; i++)
                if (Opaque[i]) { o[i * 2] = 1; o[i * 2 + 1] = Indices[i]; }
            return o;
        }

        static int U16(byte[] d, int o) => d[o] | d[o + 1] << 8;
    }
}
