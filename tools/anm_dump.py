#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Parse and decode Strahd's Possession .ANM cutscenes (CINE00-12.ANM).

usage: anm_dump.py check FILE...                  every chunk must consume exactly its bytes
       anm_dump.py frames FILE OUTDIR N[,N...]    write frames as indexed PNGs

Format:
  u16 frames, u16 ticks per frame, then per frame: u16 chunk count, chunks of u16 type, u32 size, data.
  0 palette (768 bytes, 6-bit RGB)   1 clear screen and palette    2 RLE full screen (320x400, PackBits per row)
  3 delta: vertical runs per column  4 loop (u16 count, u32 offset; not used by any file)
  5 music (u16 track, u16 mode)      6 sound (u16)   7 caption (u16 text or 0, u16)   9 speech (u16)
  8 raw 320x256 picture as four VGA planes of 0x5000 bytes, drawn at byte offset 0xff0 (row 51)
"""
import struct, sys, os, zlib, collections


def png(p, w, h, px, pal):
    c = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    raw = b''.join(b'\0' + bytes(px[y * w:(y + 1) * w]) for y in range(h))
    open(p, 'wb').write(b'\x89PNG\r\n\x1a\n' + c(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 3, 0, 0, 0)) + c(b'PLTE', bytes(min(255, v * 255 // 63) for v in pal)) + c(b'IDAT', zlib.compress(raw)) + c(b'IEND', b''))


def rle(d, s):
    p = 0
    for y in range(400):
        x = 0
        while x < 320:
            b = d[p]; p += 1
            if b & 0x80: n = 0x101 - b; s[y * 320 + x:y * 320 + x + n] = bytes([d[p]]) * n; p += 1
            else: n = b + 1; s[y * 320 + x:y * 320 + x + n] = d[p:p + n]; p += n
            x += n
        if x != 320: raise ValueError('row %d is %d wide' % (y, x))
    return p


def delta(d, s):
    u = lambda o: struct.unpack_from('<H', d, o)[0]
    n = u(0); p = 2; x = 0
    for _ in range(n):
        x += u(p); r = u(p + 2); p += 4; y = 0
        for _ in range(r):
            y += u(p); c = u(p + 2); p += 4
            for _ in range(c): s[y * 320 + x] = d[p]; p += 1; y += 1
    return p


def copy(d, s):
    for pl in range(4):
        for k in range(0x5000): s[(0xff0 + k) // 80 * 320 + (0xff0 + k) % 80 * 4 + pl] = d[pl * 0x5000 + k]
    return 0x14000


def play(b, want=None):
    """yields (frame, screen, palette, chunk types)"""
    n, dl = struct.unpack_from('<HH', b, 0); p = 4; s = bytearray(320 * 410); pal = bytes(768)
    for f in range(n):
        k, = struct.unpack_from('<H', b, p); p += 2; ts = []
        for _ in range(k):
            t, z = struct.unpack_from('<HI', b, p); d = b[p + 6:p + 6 + z]; p += 6 + z; ts.append(t)
            if len(d) != z: raise ValueError('chunk past the end')
            if t == 0: pal = d; u = 768
            elif t == 1: s = bytearray(320 * 410); pal = bytes(768); u = 0
            elif t == 2: u = rle(d, s)
            elif t == 3: u = delta(d, s)
            elif t == 8: u = copy(d, s)
            else: u = {5: 4, 6: 2, 7: 4, 9: 2}[t]
            if u != z: raise ValueError('frame %d type %d: used %d of %d' % (f, t, u, z))
        yield f, s, pal, ts
        if want and f >= max(want): return
    if not want and p != len(b): raise ValueError('%d bytes left' % (len(b) - p))


if __name__ == '__main__':
    if sys.argv[1] == 'check':
        T = collections.Counter(); tf = 0
        for f in sys.argv[2:]:
            c = collections.Counter(); m = 0
            for i, s, pal, ts in play(open(f, 'rb').read()): c.update(ts); m += 1
            print('%-12s ok, %d frames, chunks %s' % (os.path.basename(f), m, dict(sorted(c.items())))); T += c; tf += m
        print('total', tf, 'frames', dict(sorted(T.items())))
    else:
        want = {int(x) for x in sys.argv[4].split(',')}; os.makedirs(sys.argv[3], exist_ok=True)
        for i, s, pal, ts in play(open(sys.argv[2], 'rb').read(), want):
            if i in want: png(os.path.join(sys.argv[3], '%s_%04d.png' % (os.path.basename(sys.argv[2])[:-4], i)), 320, 400, s, pal)
