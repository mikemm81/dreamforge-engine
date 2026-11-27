// SPDX-License-Identifier: GPL-3.0-only
// Which RES4 entries a level's walls, floor and ceiling use.
// Documented (docs/formats/levels.md): the 64 wall types with four faces of four s16 texture numbers
// (lower-left, lower-right, upper-left, upper-right), numbers counted from the start of the wall set,
// header byte 0x009 (0 = two rows), the floor and ceiling tile numbers. The wall-set starts and the tile
// start come from INIT (docs/formats/types.md). Marked INFERRED below: worked out from the game data.
using System.Collections.Generic;

namespace DreamForge.Formats
{
    public enum FaceTextureKind
    {
        /// <summary>A type-5 wall texture.</summary>
        Wall,
        /// <summary>A type-6 column shape used as a (masked) wall; expected, not an error.</summary>
        Shape,
        /// <summary>Cannot be resolved: outside the wall set's block, outside RES4, or another kind of entry.</summary>
        Missing,
    }

    public struct FaceTexture
    {
        public int Number;
        public int Entry;
        public FaceTextureKind Kind;
        public string Problem;
    }

    public sealed class LevelTextures
    {
        /// <summary>Key for a face or tile that is not drawn from a texture (yet); drawn as a placeholder.</summary>
        public const int Placeholder = -2;

        /// <summary>
        /// INFERRED from data, not in docs: face f of a wall cell is its side facing
        /// 0 = x-1, 1 = y+1, 2 = x+1, 3 = y-1. 207 of 219 wall-mounted records (decals, switches,
        /// examine spots with exactly one open neighbour) carry the face number of that neighbour's side.
        /// </summary>
        public static int FaceTowards(int dx, int dy)
        {
            if (dx == -1 && dy == 0) return 0;
            if (dx == 0 && dy == 1) return 1;
            if (dx == 1 && dy == 0) return 2;
            if (dx == 0 && dy == -1) return 3;
            throw new System.ArgumentException($"({dx}, {dy}) is not a side");
        }

        /// <summary>Documented: header byte 0x009 = 0 means walls are two rows high.</summary>
        public static int Rows(LevelHeader h) => h.TwoRowsFlag == 0 ? 2 : 1;

        /// <summary>
        /// Documented order within a face (marked "likely" in the docs); seam continuity of the pictures
        /// that span two textures (doors, fireplace) agrees. row 0 = lower, half 0 = the viewer's left.
        /// </summary>
        public static short Number(WallFace f, int row, int half) =>
            row == 0 ? (half == 0 ? f.LowerLeft : f.LowerRight) : (half == 0 ? f.UpperLeft : f.UpperRight);

        readonly ResFile res;
        readonly LevelHeader header;
        readonly GameInit init;
        readonly bool hasSet;
        readonly int setStart, setEnd;
        readonly HashSet<string> warnings = new HashSet<string>();

        /// <summary>Distinct problems met while resolving (missing textures, unknown wall set, ...).</summary>
        public IEnumerable<string> Warnings => warnings;
        public int WallSetStart => setStart;
        public int WallSetEnd => setEnd;

        public LevelTextures(ResFile res4, LevelHeader header, GameInit init)
        {
            res = res4;
            this.header = header;
            this.init = init;
            hasSet = init.WallSetRange(header.WallSet, out setStart, out setEnd);
            if (!hasSet)
                warnings.Add($"wall set {header.WallSet} is not in INIT's table of {init.WallSetCount} sets");
            if (init.TileStart < 0 || init.TileStart >= res.Entries.Count || !IsTile(res.Entries[init.TileStart]))
                warnings.Add($"INIT's tile start {init.TileStart} is not a tile entry in RES4");
        }

        static bool IsTile(ResEntry e) => WallTexture.IsTexture(e) && e.UnpackedSize == ResEntry.TileStatedSize;
        static bool IsWall(ResEntry e) => WallTexture.IsTexture(e) && e.UnpackedSize == WallTexture.WallStatedSize;
        static bool IsShape(ResEntry e) => e.Kind == ResEntryKind.Eh && e.Type == 6;

        /// <summary>What one half-row of a face of a wall type shows.</summary>
        public FaceTexture Resolve(int wallType, int face, int row, int half)
        {
            int number = Number(header.WallTypes[wallType].Faces[face], row, half);
            var r = new FaceTexture { Number = number, Entry = -1, Kind = FaceTextureKind.Missing };
            if (!hasSet) { r.Problem = $"wall set {header.WallSet} unknown"; return r; }
            int entry = setStart + number;
            r.Entry = entry;
            if (number < 0 || entry >= setEnd)
                r.Problem = $"texture {number} is outside wall set {header.WallSet} (entries {setStart}-{setEnd - 1})";
            else if (entry >= res.Entries.Count)
                r.Problem = $"texture {number} (entry {entry}) is outside RES4";
            else if (IsWall(res.Entries[entry]))
                r.Kind = FaceTextureKind.Wall;
            else if (IsShape(res.Entries[entry]))
                r.Kind = FaceTextureKind.Shape;
            else
            {
                var e = res.Entries[entry];
                r.Problem = $"texture {number} (entry {entry}) is {(e.Kind == ResEntryKind.Eh ? $"EH type {e.Type}" : e.Kind.ToString())}";
            }
            return r;
        }

        /// <summary>
        /// RES4 entry to draw for one half-row of a face: a wall texture, or a column shape drawn as a
        /// masked wall. Placeholder (with a warning) only when it cannot be resolved.
        /// </summary>
        public int WallEntry(int wallType, int face, int row, int half)
        {
            var r = Resolve(wallType, face, row, half);
            if (r.Kind != FaceTextureKind.Missing) return r.Entry;
            warnings.Add($"wall type {wallType} face {face}: {r.Problem}");
            return Placeholder;
        }

        /// <summary>RES4 entry for floor/ceiling tile <paramref name="n"/> (INIT tile start + n), or Placeholder. -1 (sky) is the caller's job.</summary>
        public int TileEntry(int n)
        {
            int entry = init.TileStart + n;
            if (n >= 0 && entry < res.Entries.Count && IsTile(res.Entries[entry])) return entry;
            warnings.Add($"tile {n} (entry {entry}) is not a tile entry in RES4");
            return Placeholder;
        }
    }
}
