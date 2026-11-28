// SPDX-License-Identifier: GPL-3.0-only
// What happens when the player moves about a level: floor triggers, trigger lists, doors, wall switches
// and exits. Record fields and trigger operations are from docs/formats/levels.md; how they combine at
// run time is INFERRED from the data (see the notes on each constant and method) and not traced in the game.
using System;
using System.Collections.Generic;
using DreamForge.Formats;
using UnityEngine;

namespace DreamForge.World
{
    public static class RecordKind
    {
        // docs/formats/levels.md, cell records, column M.
        public const byte Decal = 0, Door = 1, WallSwitch = 2, FloorTrigger = 3, Lock = 4, Spinner = 6,
                          Exit = 8, Examine = 10, UsedUp = 11;
    }

    public static class TriggerOp
    {
        // docs/formats/levels.md, trigger lists.
        public const byte Open = 0, Close = 1, Toggle = 2, Effect = 3, SetWall = 4, Sound = 5, Music = 6, Text = 7,
                          SetFlag = 8, ClearFlag = 9, ToggleFlag = 10, Cutscene = 11, Event = 12, SpawnNpc = 13;
    }

    /// <summary>Where an exit sends the player (docs/formats/levels.md: bytes 0x06, 0x07, 0x08).</summary>
    public struct ExitTarget
    {
        public int Level;
        public Vector2Int Cell;
        /// <summary>Turn in steps of 45 degrees (high byte of 0x08).</summary>
        public int Turn;
        public CellRecord Record;

        public static ExitTarget From(CellRecord r) => new ExitTarget
        {
            Level = r.Arg6,
            Cell = new Vector2Int(r.Arg7, r.Overlay & 0xFF),
            Turn = (r.Overlay >> 8) & 0xFF,
            Record = r,
        };
    }

    public sealed class LevelPlay
    {
        /// <summary>Documented: target level 99 in M is the world map (not implemented).</summary>
        public const int WorldMapLevel = 99;

        readonly Level lv;
        readonly Dictionary<Vector2Int, List<CellRecord>> records = new Dictionary<Vector2Int, List<CellRecord>>();
        readonly Dictionary<Vector2Int, Door> doors = new Dictionary<Vector2Int, Door>();

        /// <summary>Raised when the player walks into an exit; the loader changes level.</summary>
        public event Action<ExitTarget> ExitTaken;
        /// <summary>Everything that happened, newest last (also sent to the console with a [PLAY] prefix).</summary>
        public readonly List<string> Log = new List<string>();

        public LevelPlay(Level level)
        {
            lv = level;
            foreach (var r in lv.Records)
            {
                var c = new Vector2Int(r.X, r.Y);
                if (!records.TryGetValue(c, out var list)) records[c] = list = new List<CellRecord>();
                list.Add(r);
            }
        }

        /// <summary>
        /// Wall cells with a door record. INFERRED from data, not in docs: a door is a record of kind 1 on a
        /// wall cell (257 of 261 in M; the other four, all in level 0, sit on open cells and are ignored).
        /// </summary>
        public static HashSet<Vector2Int> DoorCells(Level lv, List<string> ignored = null)
        {
            var set = new HashSet<Vector2Int>();
            foreach (var r in lv.Records)
            {
                if (r.Kind != RecordKind.Door) continue;
                if (r.X < lv.Width && r.Y < lv.Height && !lv.At(r.X, r.Y).IsOpen) set.Add(new Vector2Int(r.X, r.Y));
                else ignored?.Add($"door record at ({r.X}, {r.Y}) is not on a wall cell; ignored");
            }
            return set;
        }

        public void AddDoor(Door d) => doors[d.Cell] = d;
        public IReadOnlyDictionary<Vector2Int, Door> Doors => doors;

        public IEnumerable<CellRecord> At(Vector2Int c) =>
            records.TryGetValue(c, out var l) ? l : (IEnumerable<CellRecord>)Array.Empty<CellRecord>();

        /// <summary>True when the cell can't be walked into (a wall, or a door that isn't fully open).</summary>
        public bool Blocks(Vector2Int c)
        {
            if (c.x < 0 || c.y < 0 || c.x >= lv.Width || c.y >= lv.Height) return true;
            if (doors.TryGetValue(c, out var d)) return !(d.WantsOpen && d.IsFullyOpen);
            return !lv.At(c.x, c.y).IsOpen;
        }

        // ---- player events ----

        /// <summary>
        /// INFERRED from data, not in docs: an enabled floor trigger runs its list (0x02) when the player steps
        /// onto the cell and its second list (0x03, if not -1) on stepping off. Level 18 puts a floor trigger
        /// on each side of most doors, each toggling the door: one opens it on the way in, the other closes it
        /// behind you. An exit on an open cell is taken on stepping onto it.
        /// </summary>
        public void EnterCell(Vector2Int c)
        {
            foreach (var r in At(c))
            {
                if (r.Kind == RecordKind.FloorTrigger && r.Enabled && r.Trigger >= 0)
                    RunList(r.Trigger, $"floor trigger ({c.x}, {c.y}) entered");
                else if (r.Kind == RecordKind.Exit)
                    TakeExit(r, "stepped onto");
            }
        }

        public void LeaveCell(Vector2Int c)
        {
            foreach (var r in At(c))
                if (r.Kind == RecordKind.FloorTrigger && r.Enabled && r.Trigger2 >= 0)
                    RunList(r.Trigger2, $"floor trigger ({c.x}, {c.y}) left");
        }

        /// <summary>
        /// The player walked into wall cell <paramref name="c"/>. INFERRED from data, not in docs: an exit on a
        /// wall cell is taken by walking into it. 189 of M's 208 exits sit on wall cells at the edge of a
        /// passage (the inn's door in level 1, the edges of the outdoor maps), and the target cell is open.
        /// </summary>
        public void Bump(Vector2Int c)
        {
            foreach (var r in At(c))
                if (r.Kind == RecordKind.Exit) { TakeExit(r, "walked into"); return; }
        }

        /// <summary>
        /// The player uses face <paramref name="face"/> of wall cell <paramref name="c"/> (the face they look at).
        /// INFERRED from data, not in docs: an enabled wall switch (flag 4) on that face runs its trigger list;
        /// switches without flag 4 are pictures only (level 1's banners). A lock needs its key item (0x06),
        /// and there is no inventory yet. Returns true if anything was there to use.
        /// </summary>
        public bool Use(Vector2Int c, int face)
        {
            foreach (var r in At(c))
            {
                if (r.Face != face) continue;
                if (r.Kind == RecordKind.WallSwitch && r.Enabled && r.Trigger >= 0)
                {
                    RunList(r.Trigger, $"wall switch ({c.x}, {c.y}) face {face} used");
                    return true;
                }
                if (r.Kind == RecordKind.Lock && r.Enabled)
                {
                    Say($"lock ({c.x}, {c.y}) needs item {r.Arg6}; no inventory yet");
                    return true;
                }
                if (r.Kind == RecordKind.Examine)
                {
                    Say($"examine ({c.x}, {c.y}): picture {(r.Overlay >> 8) & 0xFF} (not shown yet)");
                    return true;
                }
            }
            return false;
        }

        // ---- trigger lists ----

        public void RunList(int index, string cause)
        {
            if (index < 0 || index >= lv.TriggerLists.Length) { Say($"{cause}: trigger list {index} does not exist"); return; }
            var list = lv.TriggerLists[index];
            Say($"{cause}: trigger list {index} ({list.Actions.Length} action(s))");
            foreach (var a in list.Actions)
            {
                var c = new Vector2Int(a.X, a.Y);
                switch (a.Operation)
                {
                    case TriggerOp.Open: SetCell(c, true, false); break;
                    case TriggerOp.Close: SetCell(c, false, false); break;
                    case TriggerOp.Toggle: SetCell(c, false, true); break;
                    default:
                        Say($"  op {a.Operation} at ({a.X}, {a.Y}) args {a.Argument} {a.Argument2}: not implemented");
                        break;
                }
            }
        }

        /// <summary>
        /// Open, close or toggle what is at a cell. INFERRED from data, not in docs: on a door this opens or
        /// closes it; on anything else it sets or clears the enabled flag (4) of the cell's records. Level 18
        /// uses close on floor triggers (lists 38, 41, 43, 45) after they've fired once.
        /// </summary>
        void SetCell(Vector2Int c, bool open, bool toggle)
        {
            if (doors.TryGetValue(c, out var d))
            {
                bool to = toggle ? !d.WantsOpen : open;
                d.SetOpen(to);
                Say($"  door ({c.x}, {c.y}) {(to ? "opens" : "closes")}");
                return;
            }
            bool any = false;
            foreach (var r in At(c))
            {
                bool on = toggle ? !r.Enabled : open;
                r.Flags = (byte)(on ? r.Flags | 0x04 : r.Flags & ~0x04);
                Say($"  record kind {r.Kind} at ({c.x}, {c.y}) {(on ? "enabled" : "disabled")}");
                any = true;
            }
            if (!any) Say($"  nothing at ({c.x}, {c.y}) to {(toggle ? "toggle" : open ? "open" : "close")}");
        }

        void TakeExit(CellRecord r, string how)
        {
            var t = ExitTarget.From(r);
            Say($"exit ({r.X}, {r.Y}) {how}: level {t.Level} cell ({t.Cell.x}, {t.Cell.y}) turn {t.Turn} x 45 deg");
            ExitTaken?.Invoke(t);
        }

        void Say(string s)
        {
            Log.Add(s);
            Debug.Log("[PLAY] " + s);
        }
    }
}
