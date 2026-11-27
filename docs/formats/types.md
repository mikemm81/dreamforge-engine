# Type tables

## Shape numbers

Every 3D object is drawn with a shape from `RES3`:

```
shape = kind_base + type_base + frame × views + view
```

`kind_base` values are `u16` in `INIT`:

| Kind | Offset (S) | S | Offset (M, P) | M | P |
|---|---|---|---|---|---|
| NPC | 0x06 | 0 | 0x0E | 25 | 3 |
| Object | 0x08 | 2169 | 0x10 | 1946 | 1565 |
| Item | 0x0A | 3316 | 0x12 | 2726 | 2110 |
| Effect | 0x0C | 4158 | 0x14 | 2976 | 2383 |

Shapes below the NPC base are shared effects.

## Wall sets and tiles in `RES4` (in `INIT`)

Checked in M; P not yet checked. Not in official documentation: these fields were found in the game's own `INIT` and confirmed against the levels (see [levels.md](levels.md#drawing-a-wall-type-likely)).

| Offset (M) | Type | Field |
|---|---|---|
| 0x16 | `u16` | `RES4` entry of floor/ceiling tile 0 (M: 976) |
| 0x2A | `u16` × 15 | `RES4` entry where each wall set starts: sets 0–13, then the end of set 13 |

Wall set *s* is the block of entries from table entry *s* up to, but not including, table entry *s* + 1. The last value equals the tile start. M's values are 0, 130, 177, 227, 277, 317, 357, 427, 517, 657, 759, 819, 896, 946, 976. A block holds both wall textures (type 5) and column shapes (type 6). A level's wall-face texture numbers, and the overlays of its wall switches and locks, count from the start of its set's block.

- **Monsters, M and P:** 6 views. The walk block is 4 frames × 6 views, with views 2 and 4 empty (the back view, 3, is used instead). Attack and death frames follow.
- **Monsters, S:** 8 views, all drawn. The walk block is usually 4 frames, then 8 more.
- **Objects with 16 views** in S use 8 shapes.

## NPC types (`NPCS.DAT`)

`u32 count`, then records of 0x58 bytes (M, P) or 0x45 (S).

| Offset (M, P) | Offset (S) | Type | Field |
|---|---|---|---|
| 0x00 | 0x00 | `char[20]` | name |
| 0x14 | 0x14 | `u16` | shape base |
| 0x16 | – | `u16` | height *(likely)* |
| 0x18 | 0x16 | `u16` | collision radius, as a fraction of a cell |
| 0x1A | 0x18 | `u8` | views |
| 0x1B | 0x19 | `u8` | walk frames |
| 0x1E, 0x1F | 0x1E, 0x1F | `u8` ×2 | damage dice and sides *(likely)* |
| 0x30 | 0x1D | `u8` | hit dice |
| 0x31 | 0x21 | `s8` | armour class |
| 0x32 | 0x22 | `u8` | THAC0 |
| 0x34 | 0x23 | `u32` | experience value |
| 0x40 | 0x31 | `u32` | resistances (half damage); S: one word for resistances and immunities |
| 0x44 | – | `u32` | immunities |
| 0x48 | 0x35 | `u32` | flags given to new NPCs |
| 0x50 | 0x3D | `u16[4]` | sounds *(likely)* |

## Item types (`OBJECTS.DAT`)

`u32 count`, then records of 0x41 bytes.

| Offset | Type | Field |
|---|---|---|
| 0x00 | `char[40]` | name |
| 0x28 | `u16` | shape base |
| 0x2A | `u8` | views |
| 0x2D | `u16` | inventory icon |
| 0x2F | `u8` | kind |
| 0x30 | `u8` | weight *(likely)* |
| 0x31 | `s8` | container number |
| 0x35 | `u8` | hands needed *(likely)* |
| 0x36 | `u8` | speed *(likely)* |
| 0x37 | `u8` | magic plus |
| 0x38–0x3A | `u8` ×3 | damage: dice, sides, bonus |
| 0x3B–0x3D | `u8` ×3 | the same against large creatures *(the bonus is unconfirmed)* |

## Object types (in `INIT`)

| | M, P | S |
|---|---|---|
| Count (`u16`) at | 0x5C | 0x24 |
| Record | 10 bytes | 7 bytes |
| Shape base (`u16`) | +0 | +0 |
| Views (`u8`) | +4 | +4 |
| Frames (`u8`, 0 = static) | +5 | +5 |
| Stacking height (`u8`) | +7 | – |
| Height offset (`s16`) | +8 *(likely)* | – |

Object types have no names. Effect types are 7-byte records: shape base at +0, views at +4, frames at +5.
