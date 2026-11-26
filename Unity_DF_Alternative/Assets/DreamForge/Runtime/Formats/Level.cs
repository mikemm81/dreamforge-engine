// SPDX-License-Identifier: GPL-3.0-only
// One level from LEVELS.DAT. Written from docs/formats/levels.md only.
using System;
using System.Collections.Generic;
using System.IO;

namespace DreamForge.Formats
{
    /// <summary>Per-game sizes of the level parts. Only Menzoberranzan has been tested.</summary>
    public sealed class LevelLayout
    {
        public string Game;
        public int HeaderSize;
        public int RecordSize;
        /// <summary>Header offset of the three s16 counts: NPCs, items, objects.</summary>
        public int CountsOffset;
        /// <summary>M, P: the header has tiles, wall types, sky, wall set, colours, light and music.</summary>
        public bool FullHeader;

        public static readonly LevelLayout Menzoberranzan = new LevelLayout
            { Game = "menzo", HeaderSize = 0x824, RecordSize = 16, CountsOffset = 0x80A, FullHeader = true };
        public static readonly LevelLayout StoneProphet = new LevelLayout
            { Game = "prophet", HeaderSize = 0x824, RecordSize = 16, CountsOffset = 0x80A, FullHeader = true };
        public static readonly LevelLayout StrahdsPossession = new LevelLayout
            { Game = "strahd", HeaderSize = 0x2A4, RecordSize = 10, CountsOffset = 0x28A, FullHeader = false };

        public static LevelLayout ForGame(string game)
        {
            switch (game?.Trim().ToLowerInvariant())
            {
                case "menzo": return Menzoberranzan;
                case "prophet": return StoneProphet;
                case "strahd": return StrahdsPossession;
                default: throw new ArgumentException($"unknown game '{game}'");
            }
        }
    }

    /// <summary>One wall face: four texture numbers, counted from the start of the wall set.</summary>
    public struct WallFace
    {
        public short LowerLeft, LowerRight, UpperLeft, UpperRight;
    }

    public sealed class WallType
    {
        public readonly WallFace[] Faces = new WallFace[4];
    }

    public sealed class LevelHeader
    {
        public int Width, Height;
        public int NpcCount, ItemCount, ObjectCount;

        // M, P only (FullHeader)
        /// <summary>Ceiling tile, -1 = sky.</summary>
        public short CeilingTile;
        public short FloorTile;
        /// <summary>0 = walls are two rows high.</summary>
        public byte TwoRowsFlag;
        public WallType[] WallTypes = Array.Empty<WallType>();
        /// <summary>0xFF = indoors.</summary>
        public byte Sky;
        public byte WallSet;
        public byte CeilingColour, FloorColour;
        /// <summary>Light level (likely).</summary>
        public short Light;
        public byte Music;
    }

    public readonly struct Cell
    {
        public readonly byte Byte0, Byte1;
        public Cell(byte b0, byte b1) { Byte0 = b0; Byte1 = b1; }

        /// <summary>Index into the header's wall types; 0 = open.</summary>
        public int WallType => Byte0 & 0x3F;
        public bool IsOpen => WallType == 0;
        public bool HasBlockingRecord => (Byte0 & 0x80) != 0;
        /// <summary>M: wall that vanishes while you stand in it.</summary>
        public bool VanishingWall => (Byte1 & 0x08) != 0;
        /// <summary>P: hazard.</summary>
        public bool Hazard => (Byte1 & 0x20) != 0;
    }

    public sealed class CellRecord
    {
        public int Column;   // the column list it was stored under
        public byte Kind, Flags;
        public sbyte Trigger, Trigger2;
        public byte Y, X;
        public byte Arg6, Arg7;
        // M, P (16-byte records)
        public short Overlay;
        public byte ClickX, ClickY, ClickWidth, ClickHeight;
        public byte Face, Row;
        // S (10-byte records): the two extra arguments
        public byte ExtraArg0, ExtraArg1;

        public bool Enabled => (Flags & 0x04) != 0;
        public bool Drawn => (Flags & 0x10) != 0;
        public bool Locked => (Flags & 0x40) != 0;
    }

    public struct Spawn
    {
        public short Type, X, Y;
        public ushort XFraction, YFraction;
        public byte Facing;
    }

    public struct TriggerAction
    {
        public byte Operation, Y, X, Argument, Argument2;
    }

    public sealed class TriggerList
    {
        /// <summary>The stored count byte (0-5 expected).</summary>
        public byte Count;
        public TriggerAction[] Actions = Array.Empty<TriggerAction>();
    }

    public sealed class Level
    {
        public const int PrefixSize = 0x1D, SpawnSize = 11, TriggerListCount = 50, TriggerListSize = 26;

        public int Index;
        public LevelLayout Layout;
        public LevelHeader Header;
        public Cell[] Cells;
        public List<CellRecord> Records = new List<CellRecord>();
        public Spawn[] NpcSpawns, ObjectSpawns, ItemSpawns;
        public TriggerList[] TriggerLists;
        public byte[] Automap;
        public int NotesLength;
        public int BytesLeftOver;
        public List<string> Warnings = new List<string>();

        public int Width => Header.Width;
        public int Height => Header.Height;

        public Cell At(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException($"({x}, {y}) outside {Width}x{Height}");
            return Cells[y * Width + x];
        }

        /// <summary>
        /// Parses one level. The prefix's section table is ignored and the parts are read in sequence.
        /// Throws InvalidDataException if a part runs past the end; consistency problems go to Warnings.
        /// </summary>
        public static Level Parse(byte[] data, int index, LevelLayout layout)
        {
            var r = new Reader(data);
            var lv = new Level { Index = index, Layout = layout };

            r.Seek(PrefixSize);
            lv.Header = ReadHeader(r.Take(layout.HeaderSize), layout);
            int w = lv.Width, h = lv.Height;
            if (w <= 0 || h <= 0)
                throw new InvalidDataException($"level {index}: size {w}x{h}");

            var cellBytes = r.Take(2 * w * h);
            lv.Cells = new Cell[w * h];
            for (int i = 0; i < w * h; i++)
                lv.Cells[i] = new Cell(cellBytes[2 * i], cellBytes[2 * i + 1]);

            for (int x = 0; x < w; x++)
            {
                int n = r.S16();
                if (n < 0) throw new InvalidDataException($"level {index}: column {x} has {n} records");
                for (int k = 0; k < n; k++)
                {
                    var rec = ReadRecord(r.Take(layout.RecordSize), layout, x);
                    lv.Records.Add(rec);
                    if (rec.X != x) lv.Warnings.Add($"record at ({rec.X}, {rec.Y}) is stored under column {x}");
                    if (rec.X >= w || rec.Y >= h) lv.Warnings.Add($"record at ({rec.X}, {rec.Y}) is outside the map");
                }
            }

            var hd = lv.Header;
            if (hd.NpcCount < 0 || hd.ItemCount < 0 || hd.ObjectCount < 0)
                throw new InvalidDataException($"level {index}: negative spawn count");
            // Counts are NPC, item, object; the lists are NPC, object, item.
            lv.NpcSpawns = ReadSpawns(r, hd.NpcCount);
            lv.ObjectSpawns = ReadSpawns(r, hd.ObjectCount);
            lv.ItemSpawns = ReadSpawns(r, hd.ItemCount);

            lv.TriggerLists = new TriggerList[TriggerListCount];
            for (int i = 0; i < TriggerListCount; i++)
            {
                var t = r.Take(TriggerListSize);
                var tl = new TriggerList { Count = t[0] };
                if (t[0] > 5) lv.Warnings.Add($"trigger list {i} has count {t[0]} (expected 0-5)");
                tl.Actions = new TriggerAction[Math.Min((int)t[0], 5)];
                for (int a = 0; a < tl.Actions.Length; a++)
                {
                    int o = 1 + 5 * a;
                    tl.Actions[a] = new TriggerAction
                        { Operation = t[o], Y = t[o + 1], X = t[o + 2], Argument = t[o + 3], Argument2 = t[o + 4] };
                }
                lv.TriggerLists[i] = tl;
            }

            int w2 = r.S16(), h2 = r.S16();
            if (w2 != w || h2 != h) lv.Warnings.Add($"repeated size {w2}x{h2} differs from {w}x{h}");

            lv.Automap = r.Take(w * h);
            long notes = r.U32();
            if (notes > r.Remaining) throw new InvalidDataException($"level {index}: {notes} bytes of notes, {r.Remaining} left");
            r.Take((int)notes);
            lv.NotesLength = (int)notes;

            lv.BytesLeftOver = r.Remaining;
            if (lv.BytesLeftOver != 0) lv.Warnings.Add($"{lv.BytesLeftOver} bytes left over after the notes");
            return lv;
        }

        static LevelHeader ReadHeader(byte[] b, LevelLayout layout)
        {
            var h = new LevelHeader
            {
                Width = S16(b, 0x001),
                Height = S16(b, 0x003),
                NpcCount = S16(b, layout.CountsOffset),
                ItemCount = S16(b, layout.CountsOffset + 2),
                ObjectCount = S16(b, layout.CountsOffset + 4),
            };
            if (!layout.FullHeader)
                return h;

            h.CeilingTile = S16(b, 0x005);
            h.FloorTile = S16(b, 0x007);
            h.TwoRowsFlag = b[0x009];
            h.WallTypes = new WallType[64];
            for (int t = 0; t < 64; t++)
            {
                var wt = new WallType();
                for (int f = 0; f < 4; f++)
                {
                    int o = 0x00A + 0x20 * t + 8 * f;
                    wt.Faces[f] = new WallFace
                    {
                        LowerLeft = S16(b, o), LowerRight = S16(b, o + 2),
                        UpperLeft = S16(b, o + 4), UpperRight = S16(b, o + 6),
                    };
                }
                h.WallTypes[t] = wt;
            }
            h.Sky = b[0x810];
            h.WallSet = b[0x811];
            h.CeilingColour = b[0x818];
            h.FloorColour = b[0x819];
            h.Light = S16(b, 0x81A);
            h.Music = b[0x81C];
            return h;
        }

        static CellRecord ReadRecord(byte[] q, LevelLayout layout, int column)
        {
            var c = new CellRecord
            {
                Column = column,
                Kind = q[0], Flags = q[1], Trigger = (sbyte)q[2], Trigger2 = (sbyte)q[3],
                Y = q[4], X = q[5], Arg6 = q[6], Arg7 = q[7],
            };
            if (layout.RecordSize >= 16)
            {
                c.Overlay = S16(q, 8);
                c.ClickX = q[10]; c.ClickY = q[11]; c.ClickWidth = q[12]; c.ClickHeight = q[13];
                c.Face = q[14]; c.Row = q[15];
            }
            else if (layout.RecordSize >= 10)
            {
                c.ExtraArg0 = q[8]; c.ExtraArg1 = q[9];
            }
            return c;
        }

        static Spawn[] ReadSpawns(Reader r, int n)
        {
            var s = new Spawn[n];
            for (int i = 0; i < n; i++)
            {
                var q = r.Take(SpawnSize);
                s[i] = new Spawn
                {
                    Type = S16(q, 0), X = S16(q, 2), XFraction = U16(q, 4),
                    Y = S16(q, 6), YFraction = U16(q, 8), Facing = q[10],
                };
            }
            return s;
        }

        static short S16(byte[] b, int o) => (short)(b[o] | b[o + 1] << 8);
        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);

        /// <summary>Bounds-checked sequential reader over one level's bytes.</summary>
        sealed class Reader
        {
            readonly byte[] b;
            int p;
            public Reader(byte[] data) { b = data; }
            public int Remaining => b.Length - p;

            public void Seek(int pos)
            {
                if (pos > b.Length) throw new InvalidDataException($"level is {b.Length} bytes, shorter than {pos}");
                p = pos;
            }

            public byte[] Take(int n)
            {
                if (n < 0 || n > b.Length - p)
                    throw new InvalidDataException($"needs {n} bytes at 0x{p:X}, level has {b.Length}");
                var r = new byte[n];
                Buffer.BlockCopy(b, p, r, 0, n);
                p += n;
                return r;
            }

            public short S16() { var q = Take(2); return (short)(q[0] | q[1] << 8); }
            public uint U32() { var q = Take(4); return (uint)(q[0] | q[1] << 8 | q[2] << 16 | q[3] << 24); }
        }
    }

    /// <summary>LEVELS.DAT: u32 count, u32 offset[count]; a level ends where the next begins.</summary>
    public sealed class LevelsFile
    {
        readonly ResFile dir;
        public string Path => dir.Path;
        public int Count => dir.Entries.Count;

        LevelsFile(ResFile d) { dir = d; }

        /// <summary>Same directory layout as a RES file, so the same checked reader is used.</summary>
        public static LevelsFile Open(string path) => new LevelsFile(ResFile.Open(path));

        public Level ReadLevel(int index, LevelLayout layout)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"level {index}; the file has {Count}");
            return Level.Parse(dir.ReadEntry(dir.Entries[index]), index, layout);
        }
    }
}
