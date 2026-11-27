// SPDX-License-Identifier: GPL-3.0-only
// INIT (M): docs/formats/types.md. Only the fields the level renderer needs are read here.
using System.Collections.Generic;
using System.IO;

namespace DreamForge.Formats
{
    /// <summary>
    /// The RES4 layout fields of INIT: where the floor/ceiling tiles start, and the table of wall-set starts.
    /// Read from the user's own INIT at run time. Offsets checked in M only.
    /// </summary>
    public sealed class GameInit
    {
        /// <summary>u16: RES4 entry of tile 0.</summary>
        public const int TileStartOffset = 0x16;
        /// <summary>u16 x 15: RES4 entry where wall set s starts; set s ends where set s + 1 starts.</summary>
        public const int WallSetTableOffset = 0x2A;
        public const int WallSetTableLength = 15;

        public int TileStart { get; private set; }
        /// <summary>15 values: the starts of wall sets 0-13, then the end of the last set.</summary>
        public int[] WallSetTable { get; private set; }
        public int WallSetCount => WallSetTable.Length - 1;
        public List<string> Warnings { get; } = new List<string>();

        public static GameInit Open(string path) => Parse(File.ReadAllBytes(path));

        public static GameInit Parse(byte[] d)
        {
            int need = WallSetTableOffset + 2 * WallSetTableLength;
            if (d.Length < need)
                throw new InvalidDataException($"INIT is {d.Length} bytes; the wall-set table needs {need}");
            var g = new GameInit { TileStart = U16(d, TileStartOffset), WallSetTable = new int[WallSetTableLength] };
            for (int i = 0; i < WallSetTableLength; i++)
                g.WallSetTable[i] = U16(d, WallSetTableOffset + 2 * i);
            for (int i = 1; i < WallSetTableLength; i++)
                if (g.WallSetTable[i] < g.WallSetTable[i - 1])
                    g.Warnings.Add($"wall-set table is not ascending at {i}: {g.WallSetTable[i - 1]} then {g.WallSetTable[i]}");
            if (g.WallSetTable[WallSetTableLength - 1] != g.TileStart)
                g.Warnings.Add($"wall-set table ends at {g.WallSetTable[WallSetTableLength - 1]}, tiles start at {g.TileStart}");
            return g;
        }

        /// <summary>First RES4 entry of wall set <paramref name="s"/> and one past its last, or false if there is no such set.</summary>
        public bool WallSetRange(int s, out int start, out int end)
        {
            start = end = 0;
            if (s < 0 || s >= WallSetCount) return false;
            start = WallSetTable[s];
            end = WallSetTable[s + 1];
            return true;
        }

        static int U16(byte[] d, int o) => d[o] | d[o + 1] << 8;
    }
}
