// SPDX-License-Identifier: GPL-3.0-only
// RES container reader. Written from docs/formats/resources.md only.
using System;
using System.Collections.Generic;
using System.IO;

namespace DreamForge.Formats
{
    public enum ResEntryKind
    {
        /// <summary>Zero-length entry (allowed by the format).</summary>
        Empty,
        /// <summary>Starts with the "EH" header.</summary>
        Eh,
        /// <summary>Has data but no "EH" header (for example RES9 scripts).</summary>
        Raw,
        /// <summary>Offset outside the file or before the previous entry.</summary>
        Invalid,
    }

    public sealed class ResEntry
    {
        public int Index;
        public long Offset;
        public long Size;
        public ResEntryKind Kind;

        /// <summary>"EH" type byte, or -1.</summary>
        public int Type = -1;
        /// <summary>"EH" header_length, or -1.</summary>
        public int HeaderLength = -1;
        /// <summary>Compression code (0 none, 1 RLE, 2 LZW, 3 LZSS) for textures and column shapes, or -1.</summary>
        public int Compression = -1;
        /// <summary>Unpacked size for textures and column shapes, or -1.</summary>
        public long UnpackedSize = -1;
        /// <summary>Absolute file offset of the (possibly compressed) data, or -1.</summary>
        public long PayloadOffset = -1;
        /// <summary>Problem found while reading the directory, or null.</summary>
        public string Problem;

        public long End => Offset + Size;

        /// <summary>One table row. tools/res_list.py prints the same format, so the two can be diffed.</summary>
        public string ToTableRow()
        {
            string kind = Kind switch
            {
                ResEntryKind.Empty => "empty",
                ResEntryKind.Eh => "EH",
                ResEntryKind.Raw => "raw",
                _ => "invalid",
            };
            return $"{Index,5} {Offset,10} {Size,8} {kind,-7} {Fmt(Type),4} {Fmt(HeaderLength),4} {Fmt(Compression),4} {Fmt(UnpackedSize),8}";
        }

        public const string TableHeader = "index     offset     size kind    type hlen comp unpacked";

        static string Fmt(long v) => v < 0 ? "-" : v.ToString();
    }

    public sealed class ResFile
    {
        public string Path { get; }
        public long FileLength { get; }
        public IReadOnlyList<ResEntry> Entries { get; }

        ResFile(string path, long length, List<ResEntry> entries)
        {
            Path = path;
            FileLength = length;
            Entries = entries;
        }

        /// <summary>
        /// Reads the directory: u32 count, u32 offset[count]. An entry ends where the next begins;
        /// the last one ends at the end of the file. Throws InvalidDataException only when the
        /// table itself can't be read; problems with single entries are recorded on the entry.
        /// </summary>
        public static ResFile Open(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);
            long length = fs.Length;
            if (length < 4)
                throw new InvalidDataException($"{path}: {length} bytes, too short for an entry count");

            uint count = br.ReadUInt32();
            long tableEnd = 4 + 4L * count;
            if (tableEnd > length)
                throw new InvalidDataException($"{path}: count {count} needs a {tableEnd}-byte table, file has {length} bytes");

            var offsets = new long[count];
            for (int i = 0; i < count; i++)
                offsets[i] = br.ReadUInt32();

            var entries = new List<ResEntry>((int)count);
            var peek = new byte[12];
            for (int i = 0; i < count; i++)
            {
                long off = offsets[i];
                long end = i + 1 < count ? offsets[i + 1] : length;
                var e = new ResEntry { Index = i, Offset = off };
                entries.Add(e);

                if (off < tableEnd || off > length)
                {
                    e.Kind = ResEntryKind.Invalid;
                    e.Problem = $"offset {off} outside data area [{tableEnd}, {length}]";
                    continue;
                }
                if (end < off || end > length)
                {
                    e.Kind = ResEntryKind.Invalid;
                    e.Problem = $"next offset {end} is before this entry or past the end of the file";
                    continue;
                }
                e.Size = end - off;
                if (e.Size == 0)
                {
                    e.Kind = ResEntryKind.Empty;
                    continue;
                }

                fs.Position = off;
                int got = fs.Read(peek, 0, (int)Math.Min(peek.Length, e.Size));
                if (got < 5 || peek[0] != (byte)'E' || peek[1] != (byte)'H')
                {
                    e.Kind = ResEntryKind.Raw;
                    continue;
                }

                e.Kind = ResEntryKind.Eh;
                e.Type = peek[2];
                e.HeaderLength = (short)(peek[3] | peek[4] << 8);
                if (e.HeaderLength < 0 || 5L + e.HeaderLength > e.Size)
                {
                    e.Problem = $"header length {e.HeaderLength} runs past the entry end";
                    continue;
                }
                ReadTypeHeader(e, peek, got);
            }
            return new ResFile(path, length, entries);
        }

        static void ReadTypeHeader(ResEntry e, byte[] p, int got)
        {
            // M, P type 5 (textures): u8 compression, u32 unpacked_size.
            if (e.Type == 5 && e.HeaderLength >= 5)
            {
                e.Compression = p[5];
                e.UnpackedSize = (uint)(p[6] | p[7] << 8 | p[8] << 16 | p[9] << 24);
                e.PayloadOffset = e.Offset + 5 + e.HeaderLength;
            }
            // M, P type 6 (column shapes): u8 compression, u16 unpacked_size, two zero bytes.
            else if (e.Type == 6 && e.HeaderLength >= 3)
            {
                e.Compression = p[5];
                e.UnpackedSize = (ushort)(p[6] | p[7] << 8);
                e.PayloadOffset = e.Offset + 5 + e.HeaderLength;
            }
            // S type 6 (textures) and type 7 (column shapes): header length 0, then u16 unpacked_size and LZSS data.
            else if ((e.Type == 6 || e.Type == 7) && e.HeaderLength == 0)
            {
                if (got < 7)
                {
                    e.Problem = "entry ends inside the unpacked size";
                    return;
                }
                e.Compression = 3;
                e.UnpackedSize = (ushort)(p[5] | p[6] << 8);
                e.PayloadOffset = e.Offset + 7;
            }
        }

        public byte[] ReadPayload(ResEntry e)
        {
            if (e.PayloadOffset < 0)
                throw new InvalidOperationException($"entry {e.Index} has no known payload");
            long n = e.End - e.PayloadOffset;
            var buf = new byte[n];
            using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Position = e.PayloadOffset;
            int read = 0;
            while (read < n)
            {
                int r = fs.Read(buf, read, (int)(n - read));
                if (r <= 0) throw new EndOfStreamException();
                read += r;
            }
            return buf;
        }

        /// <summary>
        /// Decodes a texture or column shape entry and reports whether it yields its unpacked size.
        /// Returns null when the entry is fine or has nothing to check, otherwise a short description.
        /// </summary>
        public string CheckPayload(ResEntry e)
        {
            if (e.PayloadOffset < 0 || e.UnpackedSize < 0)
                return null;
            byte[] src = ReadPayload(e);
            var result = ResCompression.Decode(src, e.Compression, (int)e.UnpackedSize, out _);
            return result.Ok ? null : result.Message;
        }
    }
}
