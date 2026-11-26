#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Export 3D-view wall and floor/ceiling textures from RES4 (EH type 5 blobs).

Blob container:
  entry = "EH", u8 type, u16 hdrlen(=5), u8 compression, u32 (type 5) / u16 (type 6) unpacked size,
  then the packed stream (compression 0 raw, 1 RLE, 3 LZSS: same codecs as sprites).
Texture layouts found by inspection:
  12290 bytes: u16 192 (column height), then 64 columns x 192 bytes, column-major
               -> wall texture 64 wide x 192 tall
   4098 bytes stated: floor/ceiling tile, 64 x 64 (row order unconfirmed). Only 4096 bytes are
               stored; decoding stops there (see docs/formats/images.md, "Tile size in M and P")
Strahd's Possession (hdrlen 0, see blob()):
   8192 bytes: 64 columns x 128 bytes, column-major -> wall texture 64 wide x 128 tall
   4096 bytes: 64 x 64 floor/ceiling tile
Other type-6 blobs (column sprites with offset tables) are not handled here.

usage: res_textures.py RESFILE OUTDIR --colors COLORS [--palette N]
       res_textures.py RESFILE --colors COLORS [--palette N] --hash
  --hash prints one line per texture instead of writing PNGs:
    entry kind WxH sha256(palette indices) sha256(RGBA)
  Pixels top row first; RGBA = palette entry scaled v*255//63 (as in the PNGs), alpha 255.
"""
import os, struct, sys, argparse, hashlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from res_sprites import lzss, rle, png

TILE_STATED, TILE = 4098, 4096  # M, P tiles: header says 4098, entries hold 64 x 64


def blob(d, e0, e1=None):
    """Decode the entry d[e0:e1] (e1 = next entry's offset; None = end of file).
    Works on the entry's own bytes only, so a short stream raises instead of reading the next entry."""
    e = d[e0:e1]
    t = e[2]
    hl = struct.unpack_from('<H', e, 3)[0]
    if hl == 0:
        # Ravenloft: Strahd's Possession (older engine): "EH", u8 type (6/7), u16 hdrlen=0,
        # u16 unpacked size, then an LZSS stream (no compression byte)
        size = struct.unpack_from('<H', e, 5)[0]
        return lzss(e, 7, size, 0xFE)[0]
    comp = e[5]
    size = struct.unpack_from('<I' if t == 5 else '<H', e, 6)[0]
    if t == 5 and size == TILE_STATED:
        size = TILE
    p = 5 + hl
    if comp == 0:
        if len(e) - p < size:
            raise ValueError(f'stored data is {len(e) - p} bytes, expected {size}')
        return e[p:p + size]
    if comp == 1:
        return rle(e, p, size)[0]
    if comp == 3:
        return lzss(e, p, size, 0xFE)[0]
    raise ValueError(f'compression {comp}')

def texture(d, e0, e1):
    """Decode one entry and lay it out as an image: (kind, width, height, palette indices top row first),
    or None if the entry is not a wall or floor/ceiling texture."""
    b = blob(d, e0, e1)
    if len(b) == 12290 and struct.unpack_from('<H', b, 0)[0] == 192:
        col = b[2:]
        return 'wall', 64, 192, bytes(col[x * 192 + y] for y in range(192) for x in range(64))
    if len(b) == 8192 and d[e0 + 3:e0 + 5] == b'\0\0':
        return 'wall', 64, 128, bytes(b[x * 128 + y] for y in range(128) for x in range(64))
    if len(b) == TILE and (d[e0 + 2] == 5 or d[e0 + 3:e0 + 5] == b'\0\0'):  # M/P tile, or S
        return 'flat', 64, 64, bytes(b)
    return None

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('res'); ap.add_argument('out', nargs='?')
    ap.add_argument('--palette', type=int, default=0)
    ap.add_argument('--colors', required=True, help="the game's own COLORS file")
    ap.add_argument('--hash', action='store_true', help='print hashes instead of writing PNGs')
    a = ap.parse_args()
    if not a.hash and a.out is None:
        ap.error('OUTDIR is required unless --hash is given')
    cd = open(a.colors, 'rb').read()
    pal = cd[1 + 768 * a.palette: 1 + 768 * (a.palette + 1)]
    d = open(a.res, 'rb').read()
    n = struct.unpack_from('<I', d, 0)[0]
    offs = struct.unpack_from(f'<{n}I', d, 4)
    if not a.hash:
        os.makedirs(a.out, exist_ok=True)
    rgb = bytes(min(255, v * 255 // 63) for v in pal)   # the same scaling as png()
    walls = flats = bad = 0
    for i in range(n):
        e0 = offs[i]
        if d[e0:e0 + 2] != b'EH' or d[e0 + 2] not in (5, 6):
            continue
        try:
            t = texture(d, e0, offs[i + 1] if i + 1 < n else len(d))
        except (IndexError, ValueError, struct.error) as ex:
            print(f'entry {i}: {ex}'); bad += 1; continue
        if t is None:
            continue
        kind, w, h, pix = t
        if kind == 'wall':
            walls += 1
        else:
            flats += 1
        if a.hash:
            rgba = b''.join(rgb[3 * p:3 * p + 3] + b'\xff' for p in pix)
            print(f'{i} {kind} {w}x{h} {hashlib.sha256(pix).hexdigest()} {hashlib.sha256(rgba).hexdigest()}')
        else:
            png(os.path.join(a.out, f'{i:04d}_{kind}_{w}x{h}.png'), w, h, pix, pal, None)
    summary = f'{walls} walls, {flats} flats, {bad} errors'
    if a.hash:
        print(f'hashed {summary}', file=sys.stderr)
    else:
        print(f'exported {summary} into {a.out}')

if __name__ == '__main__':
    main()
