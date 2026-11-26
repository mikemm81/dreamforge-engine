// SPDX-License-Identifier: GPL-3.0-only
// Which RES4 entries a level's walls, floor and ceiling use.
// Documented (docs/formats/levels.md): the 64 wall types with four faces of four s16 texture numbers
// (lower-left, lower-right, upper-left, upper-right; that order is marked "likely"), numbers counted
// from the start of the wall set, header byte 0x009 (0 = two rows), the floor and ceiling tile numbers.
// Everything marked INFERRED below is not in docs/formats; it was worked out from the game data.
using System.Collections.Generic;

namespace DreamForge.Formats
{
    public sealed class LevelTextures
    {
        /// <summary>Key for a face or tile whose texture cannot be resolved; drawn as a placeholder.</summary>
        public const int Placeholder = -2;

        /// <summary>
        /// INFERRED from data, not in docs: wall set 0 starts at RES4 entry 0. Level 1's texture numbers
        /// (all set 0) land on one consistent run of wall textures. Where other sets start is unknown.
        /// </summary>
        public static int? WallSetStart(int wallSet) => wallSet == 0 ? 0 : (int?)null;

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
        readonly int? setStart;
        readonly List<int> tileEntries = new List<int>();
        readonly HashSet<string> warnings = new HashSet<string>();

        /// <summary>Distinct problems met while resolving (unknown wall set, non-texture entries, ...).</summary>
        public IEnumerable<string> Warnings => warnings;

        public LevelTextures(ResFile res4, LevelHeader header)
        {
            res = res4;
            this.header = header;
            setStart = WallSetStart(header.WallSet);
            if (setStart == null)
                warnings.Add($"wall set {header.WallSet}: where it starts in RES4 is unknown (only set 0 is known)");
            // INFERRED from data, not in docs: tile number n is the n-th tile entry (stated size 4,098) in RES4.
            foreach (var e in res.Entries)
                if (WallTexture.IsTexture(e) && e.UnpackedSize == ResEntry.TileStatedSize)
                    tileEntries.Add(e.Index);
        }

        public int TileCount => tileEntries.Count;

        /// <summary>RES4 entry for one half-row of a face of a wall type, or Placeholder.</summary>
        public int WallEntry(int wallType, int face, int row, int half)
        {
            var f = header.WallTypes[wallType].Faces[face];
            int number = Number(f, row, half);
            if (setStart == null) return Placeholder;
            int entry = setStart.Value + number;
            if (entry < 0 || entry >= res.Entries.Count)
            {
                warnings.Add($"wall type {wallType} face {face}: texture {number} is outside RES4");
                return Placeholder;
            }
            var e = res.Entries[entry];
            if (!WallTexture.IsTexture(e) || e.UnpackedSize != WallTexture.WallStatedSize)
            {
                string what = e.Kind == ResEntryKind.Eh ? $"EH type {e.Type}" : e.Kind.ToString();
                warnings.Add($"texture {number} (entry {entry}) is {what}, not a type-5 wall; shown as a placeholder");
                return Placeholder;
            }
            return entry;
        }

        /// <summary>RES4 entry for floor/ceiling tile <paramref name="n"/>, or Placeholder. -1 (sky) is the caller's job.</summary>
        public int TileEntry(int n)
        {
            if (n >= 0 && n < tileEntries.Count) return tileEntries[n];
            warnings.Add($"tile {n} is outside the {tileEntries.Count} tiles in RES4");
            return Placeholder;
        }
    }
}
