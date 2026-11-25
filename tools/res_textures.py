#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Export 3D-view wall and floor/ceiling textures from RES4 (EH type 5 blobs).

Blob container:
  entry = "EH", u8 type, u16 hdrlen(=5), u8 compression, u32 (type 5) / u16 (type 6) unpacked size,
  then the packed stream (compression 0 raw, 1 RLE, 3 LZSS: same codecs as sprites).
Texture layouts found by inspection:
  12290 bytes: u16 192 (column height), then 64 columns x 192 bytes, column-major
               -> wall texture 64 wide x 192 tall
   4098 bytes: 64 x 64 bytes + 2 trailing bytes -> floor/ceiling tile (row order unconfirmed)
Strahd's Possession (hdrlen 0, see blob()):
   8192 bytes: 64 columns x 128 bytes, column-major -> wall texture 64 wide x 128 tall
   4096 bytes: 64 x 64 floor/ceiling tile
Other type-6 blobs (column sprites with offset tables) are not handled here.

usage: res_textures.py RESFILE OUTDIR --colors COLORS [--palette N]
"""
import os, struct, sys, argparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from res_sprites import lzss, rle, png

def blob(d, e0):
    t = d[e0 + 2]
    hl = struct.unpack_from('<H', d, e0 + 3)[0]
    if hl == 0:
        # Ravenloft: Strahd's Possession (older engine): "EH", u8 type (6/7), u16 hdrlen=0,
        # u16 unpacked size, then an LZSS stream (no compression byte)
        size = struct.unpack_from('<H', d, e0 + 5)[0]
        return lzss(d, e0 + 7, size, 0xFE)[0]
    comp = d[e0 + 5]
    size = struct.unpack_from('<I' if t == 5 else '<H', d, e0 + 6)[0]
    p = e0 + 5 + hl
    if comp == 0:
        return d[p:p + size]
    if comp == 1:
        return rle(d, p, size)[0]
    if comp == 3:
        return lzss(d, p, size, 0xFE)[0]
    raise ValueError(f'compression {comp}')

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('res'); ap.add_argument('out')
    ap.add_argument('--palette', type=int, default=0)
    ap.add_argument('--colors', required=True, help="the game's own COLORS file")
    a = ap.parse_args()
    cd = open(a.colors, 'rb').read()
    pal = cd[1 + 768 * a.palette: 1 + 768 * (a.palette + 1)]
    d = open(a.res, 'rb').read()
    n = struct.unpack_from('<I', d, 0)[0]
    offs = struct.unpack_from(f'<{n}I', d, 4)
    os.makedirs(a.out, exist_ok=True)
    walls = flats = bad = 0
    for i in range(n):
        e0 = offs[i]
        if d[e0:e0 + 2] != b'EH' or d[e0 + 2] not in (5, 6):
            continue
        try:
            b = blob(d, e0)
        except (IndexError, ValueError) as ex:
            print(f'entry {i}: {ex}'); bad += 1; continue
        if len(b) == 12290 and struct.unpack_from('<H', b, 0)[0] == 192:
            col = b[2:]
            pix = bytes(col[x * 192 + y] for y in range(192) for x in range(64))
            png(os.path.join(a.out, f'{i:04d}_wall_64x192.png'), 64, 192, pix, pal, None)
            walls += 1
        elif len(b) == 8192 and d[e0 + 3:e0 + 5] == b'\0\0':
            pix = bytes(b[x * 128 + y] for y in range(128) for x in range(64))
            png(os.path.join(a.out, f'{i:04d}_wall_64x128.png'), 64, 128, pix, pal, None)
            walls += 1
        elif len(b) == 4096 and d[e0 + 3:e0 + 5] == b'\0\0':
            png(os.path.join(a.out, f'{i:04d}_flat_64x64.png'), 64, 64, b, pal, None)
            flats += 1
        elif len(b) == 4098:
            png(os.path.join(a.out, f'{i:04d}_flat_64x64.png'), 64, 64, b[:4096], pal, None)
            flats += 1
    print(f'exported {walls} walls, {flats} flats, {bad} errors into {a.out}')

if __name__ == '__main__':
    main()
