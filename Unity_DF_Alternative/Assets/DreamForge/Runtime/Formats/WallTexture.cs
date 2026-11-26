// SPDX-License-Identifier: GPL-3.0-only
// Walls and floor/ceiling tiles in RES4, EH type 5 (M, P): docs/formats/images.md, "Walls and tiles";
// entry header and compression: docs/formats/resources.md.
using System;
using System.IO;

namespace DreamForge.Formats
{
    /// <summary>An 8-bit image of palette indices, stored top row first.</summary>
    public sealed class IndexedImage
    {
        public int Width { get; }
        public int Height { get; }
        /// <summary>Width x Height palette indices; pixel (x, y) is Pixels[y * Width + x], y = 0 at the top.</summary>
        public byte[] Pixels { get; }

        public IndexedImage(int width, int height, byte[] pixels)
        {
            if (pixels == null || pixels.Length != width * height)
                throw new ArgumentException("pixel count does not match the size");
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        /// <summary>RGBA, 4 bytes per pixel, same order as Pixels (top row first). Alpha is always 255.</summary>
        public byte[] ToRgba(Palette palette)
        {
            var o = new byte[Pixels.Length * 4];
            for (int i = 0; i < Pixels.Length; i++)
            {
                int p = Pixels[i] * 3;
                o[i * 4] = palette.Rgb[p];
                o[i * 4 + 1] = palette.Rgb[p + 1];
                o[i * 4 + 2] = palette.Rgb[p + 2];
                o[i * 4 + 3] = 255;
            }
            return o;
        }
    }

    public enum WallTextureKind { Wall, Tile }

    /// <summary>How the 64 x 64 bytes of a tile are laid out. The docs leave this unconfirmed.</summary>
    public enum TileOrder { RowMajor, ColumnMajor }

    public sealed class WallTexture
    {
        public const int WallWidth = 64, WallHeight = 192;          // u16 192, then 64 columns of 192 bytes
        public const int WallStatedSize = 2 + WallWidth * WallHeight; // 12,290
        public const int TileSide = 64;                              // stated 4,098, stored 4,096

        public int EntryIndex { get; }
        public WallTextureKind Kind { get; }
        public IndexedImage Image { get; }

        WallTexture(int index, WallTextureKind kind, IndexedImage image)
        {
            EntryIndex = index;
            Kind = kind;
            Image = image;
        }

        public static bool IsTexture(ResEntry e) => e.Kind == ResEntryKind.Eh && e.Type == 5;

        /// <summary>
        /// Decodes an EH type-5 entry. Walls (stated 12,290 bytes) become 64 wide x 192 tall; tiles
        /// (stated 4,098, of which 4,096 are stored) become 64 x 64 in <paramref name="tileOrder"/>.
        /// Only the entry's own bytes are read. Throws InvalidDataException on anything else.
        /// </summary>
        public static WallTexture Decode(ResFile file, ResEntry e, TileOrder tileOrder = TileOrder.RowMajor)
        {
            if (!IsTexture(e))
                throw new InvalidDataException($"entry {e.Index} is not an EH type-5 entry");

            WallTextureKind kind;
            if (e.UnpackedSize == WallStatedSize) kind = WallTextureKind.Wall;
            else if (e.UnpackedSize == ResEntry.TileStatedSize) kind = WallTextureKind.Tile;
            else throw new InvalidDataException($"entry {e.Index}: unpacked size {e.UnpackedSize} is neither a wall nor a tile");

            byte[] src = file.ReadPayload(e);
            var r = ResCompression.Decode(src, e.Compression, (int)e.DecodedSize, out byte[] data);
            if (!r.Ok)
                throw new InvalidDataException($"entry {e.Index}: {r.Message}");

            return new WallTexture(e.Index, kind,
                kind == WallTextureKind.Wall ? Wall(e.Index, data) : Tile(data, tileOrder));
        }

        static IndexedImage Wall(int index, byte[] data)
        {
            int height = data[0] | data[1] << 8;
            if (height != WallHeight)
                throw new InvalidDataException($"entry {index}: wall column height {height}, expected {WallHeight}");
            // Stored column by column, each top to bottom: pixel (x, y) = data[2 + x * 192 + y].
            var px = new byte[WallWidth * WallHeight];
            for (int x = 0; x < WallWidth; x++)
            for (int y = 0; y < WallHeight; y++)
                px[y * WallWidth + x] = data[2 + x * WallHeight + y];
            return new IndexedImage(WallWidth, WallHeight, px);
        }

        static IndexedImage Tile(byte[] data, TileOrder order)
        {
            var px = new byte[TileSide * TileSide];
            if (order == TileOrder.RowMajor)
                Buffer.BlockCopy(data, 0, px, 0, px.Length);
            else
                for (int x = 0; x < TileSide; x++)
                for (int y = 0; y < TileSide; y++)
                    px[y * TileSide + x] = data[x * TileSide + y];
            return new IndexedImage(TileSide, TileSide, px);
        }
    }
}
