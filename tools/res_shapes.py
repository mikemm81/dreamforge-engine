#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Export 3D-view column shapes (EH type 6 blobs) from RES3 / RES4 to RGBA PNG.

Format:
  blob header (u16): width, left, right, top, bottom
     width  = canvas width (104 / 64 / 128); the engine centres it on the screen x
     left..right = first/last column present; offset table has right-left+1 entries
     top/bottom  = vertical extent (rows) inside a 192-row canvas (wall height)
  +0x0A : u16 column offset table (relative to blob start)
  pixel pool, then per-column commands of 3 x u16: (end*2, s16 src, start*2), terminated by end == 0.
     rows start..end-1 take pixel  blob[src + y]   (Wolfenstein-3D style compiled shape)
  Strahd's Possession stores shapes as EH type 7 (hdrlen 0, u16 size, LZSS) - same shape layout.
  Blobs of <= 16 bytes are placeholder stubs and are skipped.
  RES3 = library 3 (monsters / NPCs / items); RES4 = library 4 (masked wall overlays).

usage: res_shapes.py RESFILE OUTDIR --colors COLORS [--palette N] [--canvas-h H] [--crop]
"""
import os, struct, sys, zlib, argparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from res_textures import blob

CANVAS_H = 192

def decode(b):
    w, left, right, top, bot = struct.unpack_from('<5H', b, 0)
    width = max(w, right + 1)
    pix = {}
    for k in range(right - left + 1):
        p = struct.unpack_from('<H', b, 10 + 2 * k)[0]
        while True:
            end = struct.unpack_from('<H', b, p)[0]
            if end == 0:
                break
            src, start = struct.unpack_from('<hH', b, p + 2)
            p += 6
            for y in range(start // 2, end // 2):
                pix[(left + k, y)] = b[src + y]
    return width, pix

def png_rgba(path, w, h, rows):
    def chunk(t, c):
        return struct.pack('>I', len(c)) + t + c + struct.pack('>I', zlib.crc32(t + c) & 0xffffffff)
    raw = b''.join(b'\0' + r for r in rows)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('res'); ap.add_argument('out')
    ap.add_argument('--palette', type=int, default=0)
    ap.add_argument('--colors', required=True, help="the game's own COLORS file")
    ap.add_argument('--canvas-h', type=int, default=CANVAS_H, help='canvas height (192 Menzo/Prophet, 128 Strahd?)')
    ap.add_argument('--crop', action='store_true', help='crop to drawn pixels (default: full width x 192 canvas)')
    a = ap.parse_args()
    cd = open(a.colors, 'rb').read()
    pal = cd[1 + 768 * a.palette: 1 + 768 * (a.palette + 1)]
    rgb = [bytes(min(255, v * 255 // 63) for v in pal[3 * i:3 * i + 3]) for i in range(256)]
    d = open(a.res, 'rb').read()
    n = struct.unpack_from('<I', d, 0)[0]
    offs = struct.unpack_from(f'<{n}I', d, 4)
    os.makedirs(a.out, exist_ok=True)
    done = stubs = bad = 0
    for i in range(n):
        e0 = offs[i]
        if d[e0:e0 + 2] != b'EH' or d[e0 + 2] not in (6, 7):
            continue
        if d[e0 + 2] == 6 and d[e0 + 3:e0 + 5] == b'\0\0':
            continue  # Strahd's Possession: type 6 = wall/flat texture, not a shape
        try:
            b = blob(d, e0)
            if len(b) <= 16:
                stubs += 1; continue
            width, pix = decode(b)
        except (IndexError, ValueError, struct.error) as ex:
            print(f'entry {i}: {ex}'); bad += 1; continue
        if not pix:
            stubs += 1; continue
        x0, y0, x1, y1 = 0, 0, width, max(a.canvas_h, max(y for _, y in pix) + 1)
        if a.crop:
            xs = [x for x, _ in pix]; ys = [y for _, y in pix]
            x0, y0, x1, y1 = min(xs), min(ys), max(xs) + 1, max(ys) + 1
        rows = []
        for y in range(y0, y1):
            r = bytearray()
            for x in range(x0, x1):
                c = pix.get((x, y))
                r += rgb[c] + b'\xff' if c is not None else b'\0\0\0\0'
            rows.append(bytes(r))
        tag = f'_at{x0}x{y0}' if a.crop else ''
        png_rgba(os.path.join(a.out, f'{i:04d}_{x1 - x0}x{y1 - y0}{tag}.png'), x1 - x0, y1 - y0, rows)
        done += 1
    print(f'exported {done} shapes, skipped {stubs} stubs, {bad} errors into {a.out}')

if __name__ == '__main__':
    main()
