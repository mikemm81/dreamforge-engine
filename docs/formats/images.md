# Palettes, sprites, textures and shapes

## Palettes (`COLORS`)

`u8 count`, then `count` × 768 bytes: 256 RGB triples with 6 bits per component (0–63).

| Game | Palettes | Which to use |
|---|---|---|
| S | 15 | 0 for sprites, shapes and walls |
| M | 41 | 0 for sprites, portraits and 3D art. Some full-screen pictures need another, not yet identified |
| P | 94 | 2 for common art; area = `wall_set + 4` (see [levels.md](levels.md)); examine picture + 65; dialogue scene = script number + 18 |

## Sprites (type 1)

Eight-byte header, then one continuous compressed stream:

| Type | Field |
|---|---|
| `s16` | number of sprites |
| `s16` | height |
| `u8` | width ÷ 8 |
| `u8` | transparent colour, `0xFF` = none |
| `u8` | compression |
| `u8` | flag |

Each sprite is `height × width` bytes, left to right and top to bottom. A large picture is stored as many sprites of height 1, one per row.

Type 2 has the same header, at the end of the data.

## Walls and tiles (`RES4`)

| | Walls | Floor/ceiling tiles |
|---|---|---|
| M, P (type 5) | `u16 192`, then 64 columns of 192 bytes, top to bottom | 64 × 64 bytes, then 2 bytes of unknown meaning |
| S (type 6) | 64 columns of 128 bytes | 64 × 64 bytes |

Tell them apart by unpacked size: 12,290 and 4,098 bytes (M, P), or 8,192 and 4,096 (S). Whether tiles are stored by row or by column is unconfirmed.

## Column shapes (type 6 in M and P, type 7 in S)

Creatures, objects, items and masked wall overlays. Only drawn pixels are stored, as vertical runs.

| Offset | Type | Field |
|---|---|---|
| 0x00 | `u16` | canvas width |
| 0x02 | `u16` | first column present |
| 0x04 | `u16` | last column present |
| 0x06 | `u16` | first row |
| 0x08 | `u16` | last row (about one past the end) |
| 0x0A | `u16[last − first + 1]` | offset of each column's run list, from the start of the shape |

A run list is a series of `u16 end × 2`, `s16 source`, `u16 start × 2`, and ends when the first value is 0. For rows `start` to `end − 1`, the pixel is the byte at `source + row`.

Rows are in a space 192 high (M, P) or 128 high (S, *likely*), the same as the walls. Shapes of 16 bytes or less are empty placeholders.
