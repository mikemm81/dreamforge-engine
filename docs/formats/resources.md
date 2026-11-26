# Resource files (`RES*`)

Same container in all three games.

| Part | Content |
|---|---|
| `u32 count` | number of entries |
| `u32 offset[count]` | absolute file offsets. An entry ends where the next begins. Entries can be empty |

Most entries start with a 5-byte header, followed by `header_length` bytes of type-specific header and then the data:

```
"EH"   u8 type   s16 header_length
```

| Type | Content |
|---|---|
| 1 | Sprites, see [images.md](images.md) |
| 2 | Sprites with the 8-byte sprite header at the end of the data |
| 3 | XMIDI music |
| 4 | Digital audio |
| 5 | M, P: textures. Header is `u8 compression`, `u32 unpacked_size` |
| 6 | M, P: column shapes. Header is `u8 compression`, `u16 unpacked_size`, two zero bytes |
| 6 | **S**: textures. Header length 0, then `u16 unpacked_size` and LZSS data |
| 7 | **S**: column shapes, stored like its type 6 |

Entries without `"EH"` in `RES9` are [scripts](scripts.md).

## Contents

| File | S | M | P |
|---|---|---|---|
| `RES0`–`RES2`, `RES7`, `RES8` | 2D pictures | same | same |
| `RES3` | 3D shapes | same | same |
| `RES4` | walls and tiles | + masked wall overlays | + masked wall overlays |
| `RES5` | music | same | same |
| `RES6` | audio | same | same |
| `RES9` | pictures, scripts | same | same |
| `RES10` | speech | speech | speech |
| `RES11`, `RES12` | other sets, not read by the game as far as known | – | – |

The last `RES4` entry of M and P doesn't decode (the data runs out). It is a floor or ceiling tile. Tiles hold 4,096 bytes, not the 4,098 their header gives, so a decoder that reads to 4,098 runs out of data on the last entry. On the other tiles it reads silently into the next entry. See [images.md](images.md#walls-and-tiles-res4). In general, a decoder should never read past the end of its entry.

## Compression

| Code | Method |
|---|---|
| 0 | none |
| 1 | RLE |
| 2 | LZW: defined, never used |
| 3 | LZSS |

**RLE.** Read a signed control byte `c`. If `c < 0`, repeat the next byte `1 − c` times. Otherwise copy the next `c + 1` bytes.

**LZSS.** A ring buffer of 4,096 bytes, filled with `0xFE`, with the write position at `0xFEE`.

- Read a flag byte and use its bits from the least significant one up. When the bits run out, read another.
- Bit 1: copy one literal byte to the output and the ring.
- Bit 0: read `b0` and `b1`. The ring position is `b0 | (b1 & 0xF0) << 4` and the length is `(b1 & 0x0F) + 3`. Copy byte by byte, because source and destination can overlap.
- Stop when the output has the expected size.
