# Saves (`SAVE.DAT`)

## *Menzoberranzan*

| Part | Size |
|---|---|
| Name | 50 |
| State block: plot flags first (32 bytes), later position, level and options | 0x2F2F |
| Ticks, clock | 4 + 4 |
| Item on the cursor | 4 |
| Party | 4 × 0x41C |
| Effects: `u32 n`, then `n` records of 0x10 + 0x24 | 4 + n × 0x34 |
| Game time | 4 |
| 94 containers, each `capacity` × `s16` items, 1 byte of slot bits, `capacity` extra bytes | varies |
| State | 0x14 |
| Built-in characters | 9 × 0x41C |
| State | 0x10 |

Container capacities are in the program, not in the save. Level and position in the state block: `s32` level at 0x38, `s32` y at 0x3C, `s32` x at 0x40 (16.16), `s32` angle at 0x44.

## *Stone Prophet*

The same, with a state block of 0x36EC bytes, effect records of 0x10 + 0x2C, and 8 bytes of version text at the end.

## *Strahd's Possession*

9,288 bytes if no effects are active.

| Part | Size |
|---|---|
| Name | 50 |
| Plot flags | 32 |
| Level, x, y (16.16), angle | 4 × 4 |
| State | 2 + 2 + 4 + 2 |
| Ticks, clock | 4 + 4 |
| Held item, a bit | 2 + 1 |
| Party | 4 × 0x248 |
| Effects: `u32 n`, then `n` records | 4 + n × 0x16 |
| State, game time | 8 + 4 |
| 62 containers, each `capacity` × `s16` items and 1 byte of slot bits | varies |
| State | 4 + 4 + 15 |
| Built-in characters | 10 × 0x248 |
| State | 16 |

## Character record

0x41C bytes in M and P, 0x248 in S. The first 0x6A bytes are the same in all three.

| Offset | Type | Field |
|---|---|---|
| 0x00 | `char[16]` | name |
| 0x10 | `u16` | status; 0x8000 = slot empty (M, P: or dead) |
| 0x12 | `s8` | portrait *(likely)* |
| 0x13 | `u8` | sex *(likely)* |
| 0x14 | `u8` | race |
| 0x15 | `s8[3]` | classes, −1 = none |
| 0x1A | `u8` | alignment |
| 0x1B | `s8` | base armour class |
| 0x1C, 0x1E | `s16` ×2 | hit points, maximum |
| 0x20 | `s16[3]` | level in each class |
| 0x26 | `u32[3]` | experience in each class |
| 0x32 | `u8` ×12 | strength, dexterity, constitution, intelligence, wisdom, charisma: base and current value of each |
| 0x3E, 0x3F | `u8` ×2 | exceptional strength, base and current |
| 0x40 | `s16[12]` | backpack items, −1 = empty |
| 0x58 | `s16[2]` | rings *(likely)* |
| 0x5C | `s16[2]` | right and left hand |
| 0x60–0x68 | `s16` ×5 | armour, helm, cloak *(likely)*, quiver, gauntlets |
| 0x6A | 8 rows | mage spells, then 8 rows of cleric spells: 18 bytes a row, or 10 in S |

THAC0 is a `u8` at 0x18A (M, P) or 0x10A (S). After it come timers and lasting effects: 58 in M and P, 26 in S.
