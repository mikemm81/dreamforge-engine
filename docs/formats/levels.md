# Levels (`LEVELS.DAT`)

`u32 count`, then `count` × `u32` offsets. Levels: S 32, M 56 (21 of them identical placeholders), P 43.

## Parts of a level, in order

| Part | Size |
|---|---|
| Prefix: `u8 0` and seven `u32` section offsets. **Ignore it**: in M it is out of date for 21 levels | 0x1D |
| Header | 0x824 (S: 0x2A4) |
| Map cells | width × height × 2 |
| For each map column: `s16 n`, then `n` cell records | 2 + n × 16 (S: n × 10) |
| NPC spawns, object spawns, item spawns | 11 each |
| Trigger lists | 50 × 26 |
| `s16 width`, `s16 height` again | 4 |
| Automap | width × height |
| `u32 n`, then automap notes | 4 + n |

Read the parts in sequence.

## Header

| Offset (M, P) | Offset (S) | Type | Field |
|---|---|---|---|
| 0x001 | 0x001 | `s16` | width |
| 0x003 | 0x003 | `s16` | height |
| 0x005 | | `s16` | ceiling tile, −1 = sky |
| 0x007 | | `s16` | floor tile |
| 0x009 | | `u8` | 0 = walls are two rows high |
| 0x00A | | 64 × 0x20 | wall types |
| 0x80A | 0x28A | `s16` | number of NPCs |
| 0x80C | 0x28C | `s16` | number of **items** |
| 0x80E | 0x28E | `s16` | number of **objects** |
| 0x810 | | `u8` | sky, `0xFF` = indoors |
| 0x811 | | `u8` | wall set |
| 0x818, 0x819 | | `u8` ×2 | plain ceiling and floor colours |
| 0x81A | 0x29A | `s16` | light *(likely)* |
| 0x81C | 0x29C | `u8` | music (S: `u16`) |

S also has `s16` sky tile at 0x290 (−1 = outdoors), and at 0x292 three `s16` monster types that come back elsewhere when killed.

The counts are in the order NPC, item, object, but the spawn lists are in the order NPC, object, item.

A wall type (M, P) has four faces. Each has four `s16` texture numbers, counted from the start of the wall set: left and right half of the lower row, then of the upper row *(the order within a face is likely)*.

## Map cells

| Byte | Bits | Meaning |
|---|---|---|
| 0 | 0x3F | wall type, 0 = open |
| 0 | 0x40 | seen this frame (run time) |
| 0 | 0x80 | has a blocking record |
| 1 | 0x08 | M: wall that vanishes while you stand in it |
| 1 | 0x20 | P: hazard |

## Cell records (M, P)

| Offset | Type | Field |
|---|---|---|
| 0x00 | `u8` | kind |
| 0x01 | `u8` | flags: 4 enabled, 0x10 drawn, 0x40 locked, low two bits door type or state |
| 0x02 | `s8` | trigger list to run, −1 = none |
| 0x03 | `s8` | floor trigger: list to run on leaving. Lock: difficulty |
| 0x04, 0x05 | `u8` ×2 | y, x |
| 0x06 | `u8` | door: position 0–8. Lock: key item. Exit: target level. Spinner: 0 = turn 180° |
| 0x07 | `u8` | door: speed. Exit: target x |
| 0x08 | `s16` | wall overlay (`RES4`). Exit: low byte target y, high byte turn × 45°. Examine: high byte picture |
| 0x0A–0x0D | `u8` ×4 | clickable area: x, y, width, height |
| 0x0E | `u8` | wall face 0–3 |
| 0x0F | `u8` | 0–1 upper row, 2–3 lower row |

S has the first eight bytes the same and two more arguments.

| Kind | M | P |
|---|---|---|
| 0 | decal | decal |
| 1 | door | door |
| 2 | wall switch | wall switch |
| 3 | floor trigger | floor trigger |
| 4 | lock | lock |
| 6 | spinner | spinner |
| 7 | – | forced movement |
| 8 | exit (also S) | pit |
| 9 | – | exit |
| 10 | examine | – |
| 11 | used up | examine |

Target level 99 in M is the world map.

## Spawns

`s16 type`, `s16 x`, `u16 x fraction`, `s16 y`, `u16 y fraction`, `u8 facing` (0–7).

An item placed at exactly an NPC's position is carried by that NPC (M).

## Trigger lists

`u8 count` (0–5), then five actions of five bytes: `u8 operation`, `u8 y`, `u8 x`, `u8 argument`, `u8 argument 2`.

| Op | Action | Op | Action |
|---|---|---|---|
| 0 | open or enable | 7 | show text |
| 1 | close or disable | 8 | set plot flag |
| 2 | toggle | 9 | clear plot flag |
| 3 | fire an effect | 10 | toggle plot flag |
| 4 | set wall | 11 | cutscene |
| 5 | sound | 12 | raise an event |
| 6 | music | 13 | spawn an NPC |

## Extra files in *Stone Prophet*

- **`LEVINFO`**: 16 records of 0x801 bytes, each the "walls two rows high" byte and 64 wall types, for the 4 × 4 regions of the 256 × 256 level.
- **`TABLES`**: 15 records of 0x12400 bytes, one per wall set: four shading tables of 0x900 bytes and one table of 0x10000 bytes of unknown use.

## Saved levels (`LEVnnn.DAT`)

The same order, with these differences (M):

- the section offsets are correct;
- the cell records come in two sets of per-column lists, with a byte (moving doors) between them;
- live NPCs, objects and items replace the spawns. Each kind is stored as all its 16-byte positions, then all its state records: 0x2E bytes per NPC, 0x24 per object or item. A `u16` count precedes the objects.

State records in P are 0x32 and 0x2C bytes. In S a live NPC, object, item or effect is one 0x16-byte record.
