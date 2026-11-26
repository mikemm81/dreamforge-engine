// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using System.Text;

namespace DreamForge.Formats
{
    /// <summary>
    /// Plain-text summary of a parsed level: header fields, an ASCII map, a wall-type map and the
    /// cell records. "level_dump.py GAMEDIR map N" prints the same text, so the two can be diffed.
    /// </summary>
    public static class LevelReport
    {
        /// <summary>Wall type 0-63 as one character.</summary>
        public const string WallTypeChars = ".123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ$%";

        /// <summary>M record kinds by map letter, highest priority first when a cell has several.</summary>
        static readonly (int kind, char letter)[] KindLetters =
        {
            (8, 'X'), (1, 'D'), (4, 'L'), (2, 'S'), (3, 'T'), (6, '@'), (10, 'E'), (11, 'u'), (0, 'd'),
        };

        public static string Format(Level lv)
        {
            var h = lv.Header;
            int w = lv.Width, ht = lv.Height;
            var sb = new StringBuilder();
            sb.Append($"level {lv.Index}: {w}x{ht}\n");
            sb.Append($"header ceiling {h.CeilingTile} floor {h.FloorTile} two_rows_flag {h.TwoRowsFlag} sky {h.Sky} " +
                      $"wallset {h.WallSet} colours {h.CeilingColour} {h.FloorColour} light {h.Light} music {h.Music}\n");
            int used = lv.TriggerLists.Count(t => t.Actions.Length > 0);
            sb.Append($"counts npcs {h.NpcCount} items {h.ItemCount} objects {h.ObjectCount} records {lv.Records.Count} " +
                      $"trigger_lists_used {used} automap {lv.Automap.Length} notes {lv.NotesLength} left_over {lv.BytesLeftOver}\n");

            // Highest-priority record letter per cell
            var letters = new char[w * ht];
            var rank = new int[w * ht];
            foreach (var r in lv.Records)
            {
                if (r.X >= w || r.Y >= ht) continue;
                int i = r.Y * w + r.X;
                int p = System.Array.FindIndex(KindLetters, k => k.kind == r.Kind);
                char c = p < 0 ? '?' : KindLetters[p].letter;
                int score = p < 0 ? KindLetters.Length : p;   // unknown kinds rank lowest
                if (letters[i] == 0 || score < rank[i]) { letters[i] = c; rank[i] = score; }
            }

            sb.Append("map (x across, y down; . open, # wall, ~ vanishing wall, letters = records)\n");
            for (int y = 0; y < ht; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    var cell = lv.Cells[i];
                    sb.Append(letters[i] != 0 ? letters[i] : cell.IsOpen ? '.' : cell.VanishingWall ? '~' : '#');
                }
                sb.Append('\n');
            }

            sb.Append("wall types (. = 0, 1-9 a-z A-Z = 1-61, $ = 62, % = 63)\n");
            for (int y = 0; y < ht; y++)
            {
                for (int x = 0; x < w; x++)
                    sb.Append(WallTypeChars[lv.Cells[y * w + x].WallType]);
                sb.Append('\n');
            }

            sb.Append("records (x y kind flags trig trig2 a6 a7 overlay click face row)\n");
            foreach (var r in lv.Records)
                sb.Append($"rec {r.X} {r.Y} {r.Kind} {r.Flags:x2} {r.Trigger} {r.Trigger2} {r.Arg6} {r.Arg7} {r.Overlay} " +
                          $"{r.ClickX},{r.ClickY},{r.ClickWidth},{r.ClickHeight} {r.Face} {r.Row}\n");
            return sb.ToString();
        }

        public const string Legend =
            "X exit, D door, L lock, S wall switch, T floor trigger, @ spinner, E examine, u used up, d decal, ? other";
    }
}
