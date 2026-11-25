# Cutscenes

## `.DFA` (*Menzoberranzan*, *Stone Prophet*)

FFmpeg decodes this format under the name "Chronomaster DFA".

**Header, 128 bytes:** `"DFIA"`, `u8 0`, `u8 1` (planar), `u16 frames`, `u16 width`, `u16 height`, `u32 milliseconds per frame`, `u32 offset of the first chunk`, zeros.

**Chunk:** `char[4] name`, `u32 size`, `u32 type`, then `size` bytes. Use the type and ignore the name.

Each chunk draws over the previous frame. The frame buffer holds **four planes one after another**, each `width ÷ 4 × height` bytes; plane `p` holds the pixels with `x mod 4 = p`. Words are 16 bits.

| Type | Name | Data |
|---|---|---|
| 0 | EOFR | end of frame |
| 1 | PAL1 | 256 RGB triples, 6 bits per component |
| 2 | COPY | raw bytes |
| 3 | TSW1 | `u32` token count, `u32` start position. Control words of 16 bits, lowest first: 1 = match, 0 = literal word |
| 4 | BDLT | `u16` first line, `u16` lines. Per line: `u8` segments, each `u8` bytes to skip and `s8 n`: `n` literal bytes, or one byte repeated `−n` times |
| 5 | WDLT | `u16` lines. Per line: optional prefix words, then `u16` segments, each `u8` bytes to skip and `s8 n`: `n` literal words, or one word repeated `−n` times |
| 6 | TDLT | `u32` token count. Token: `u8` words to copy, `u8` words to skip **first** |
| 7 | DSW1 | `u16` token count. Control words of eight 2-bit codes, lowest first: lowest bit set = match, 0 = literal word, 2 = skip the following `u16` bytes |
| 8 | BLCK | clear the frame (P only) |

- **Match word:** length is `(top 3 bits + 2)` words, distance back is the low 13 bits, in words. Source and destination can overlap. A distance of 0 leaves the area as it is.
- **WDLT prefixes:** a value of 0xC000 or more skips `0x10000 − value` lines. From 0x8000 to 0xBFFF, its low byte is the last byte of the line.
- TSW1 and DSW1 chunks can end with an unused control word.

## `.ANM` (*Strahd's Possession*)

`u16 frames`, `u16 ticks per frame`. Per frame: `u16` number of chunks, each `u16 type`, `u32 size`, data.

| Type | Data |
|---|---|
| 0 | palette, 768 bytes |
| 1 | none: clear screen and palette |
| 2 | 320 × 400 screen, RLE, each row separately |
| 3 | `u16` columns. Per column: `u16` columns to move right, `u16` runs. Per run: `u16` rows to skip, `u16` length, bytes going down |
| 4 | loop: `u16` count, `u32` offset (never used) |
| 5 | music: `u16` track, `u16` mode |
| 6 | sound: `u16` |
| 7 | caption: `u16` text or 0, `u16` |
| 8 | 320 × 256 picture as four planes of 0x5000 bytes, starting at byte 0xFF0 of each plane (80 bytes a row) |
| 9 | speech: `u16` |

## `.ACT` (*Stone Prophet*)

Event track for the `.DFA` of the same name, made of 16-bit signed words: a list of voice or caption numbers to preload, a list of sounds to preload, then one list per frame. Each list ends with −1.

A frame's list has pairs of event and argument: −50 caption, −51 music, −52 sound. Some negative arguments mean stop or fade.

The number of lists matches the number of frames in 30 of the 32 files that have a `.DFA` of the same name.
