// SPDX-License-Identifier: GPL-3.0-only
// COLORS: docs/formats/images.md, "Palettes".
using System;
using System.Collections.Generic;
using System.IO;

namespace DreamForge.Formats
{
    /// <summary>One 256-colour palette, already converted to 8 bits per component.</summary>
    public sealed class Palette
    {
        public const int Colours = 256;

        /// <summary>256 x (r, g, b), 8 bits each.</summary>
        public byte[] Rgb { get; }

        public Palette(byte[] rgb)
        {
            if (rgb == null || rgb.Length != Colours * 3)
                throw new ArgumentException($"a palette is {Colours * 3} bytes");
            Rgb = rgb;
        }
    }

    /// <summary>
    /// The COLORS file: u8 count, then count x 768 bytes of RGB triples with 6-bit components (0-63).
    /// </summary>
    public sealed class ColorsFile
    {
        public const int PaletteBytes = Palette.Colours * 3;

        public string Path { get; }
        public int Count { get; }
        /// <summary>Problems that did not stop the read (size mismatch, components above 63).</summary>
        public List<string> Warnings { get; } = new List<string>();

        readonly byte[] raw;   // the 6-bit components, count x 768

        ColorsFile(string path, int count, byte[] raw)
        {
            Path = path;
            Count = count;
            this.raw = raw;
        }

        /// <summary>
        /// 6-bit to 8-bit, the project's convention (images.md): v * 255 / 63 with integer division,
        /// so 0 -> 0 and 63 -> 255. Values above 63 are not valid; they are clamped to 255.
        /// </summary>
        public static byte Scale6To8(byte v) => (byte)Math.Min(255, v * 255 / 63);

        public static ColorsFile Open(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 1)
                throw new InvalidDataException("COLORS is empty");
            int count = d[0];
            long expected = 1L + (long)count * PaletteBytes;
            var warnings = new List<string>();
            if (d.Length < expected)
                throw new InvalidDataException($"COLORS says {count} palettes ({expected} bytes) but is {d.Length} bytes");
            if (d.Length > expected)
                warnings.Add($"{d.Length - expected} bytes after the last palette ignored");

            var raw = new byte[count * PaletteBytes];
            Buffer.BlockCopy(d, 1, raw, 0, raw.Length);
            int over = 0;
            foreach (byte v in raw) if (v > 63) over++;
            if (over > 0)
                warnings.Add($"{over} component(s) above 63, clamped to 255");

            var f = new ColorsFile(path, count, raw);
            f.Warnings.AddRange(warnings);
            return f;
        }

        public Palette Get(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"palette {index} of {Count}");
            var rgb = new byte[PaletteBytes];
            for (int i = 0; i < PaletteBytes; i++)
                rgb[i] = Scale6To8(raw[index * PaletteBytes + i]);
            return new Palette(rgb);
        }
    }
}
