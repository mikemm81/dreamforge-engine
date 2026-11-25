#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Decode EH type-1 sprite resources from DreamForge RES files to PNG.

Format:
  RES file : u32 count, count x u32 absolute entry offsets
  entry    : "EH", u8 type, u16 hdrlen(=8), then sprite header + data
  sprite hdr (8 bytes):
     s16 count, s16 height, u8 width/8, u8 transparent (0xFF = none),
     u8 compression (0 raw, 1 RLE, 2 LZW-unsupported, 3 LZSS), u8 flag
  pixel stream: one continuous stream; each sprite = height * width bytes, linear chunky.
  Palette: COLORS = u8 n, n x 768 bytes of 6-bit VGA RGB.

usage: res_sprites.py RESFILE OUTDIR --colors COLORS [--palette N] [--max K]
"""
import os, struct, sys, zlib, argparse

def lzss(src, pos, n_out, fill):
    ring = bytearray([fill]) * 4096
    r = 0xFEE
    out = bytearray()
    flags = 0
    while len(out) < n_out:
        flags >>= 1
        if not flags & 0x100:
            flags = src[pos] | 0xFF00; pos += 1
        if flags & 1:
            c = src[pos]; pos += 1
            out.append(c); ring[r] = c; r = (r + 1) & 0xFFF
        else:
            lo, hi = src[pos], src[pos + 1]; pos += 2
            off = lo | ((hi & 0xF0) << 4)
            for k in range((hi & 0x0F) + 3):
                c = ring[(off + k) & 0xFFF]
                out.append(c); ring[r] = c; r = (r + 1) & 0xFFF
    return bytes(out[:n_out]), pos

def rle(src, pos, n_out):
    out = bytearray()
    while len(out) < n_out:
        c = struct.unpack_from('b', src, pos)[0]; pos += 1
        if c < 0:
            out += bytes([src[pos]]) * (1 - c); pos += 1
        else:
            out += src[pos:pos + c + 1]; pos += c + 1
    return bytes(out[:n_out]), pos

def png(path, w, h, pix, pal, trans):
    def chunk(t, b):
        return struct.pack('>I', len(b)) + t + b + struct.pack('>I', zlib.crc32(t + b) & 0xffffffff)
    raw = b''.join(b'\0' + pix[y * w:(y + 1) * w] for y in range(h))
    plte = bytes(min(255, v * 255 // 63) for v in pal)
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 3, 0, 0, 0))
    data += chunk(b'PLTE', plte)
    if trans is not None:
        data += chunk(b'tRNS', b'\xff' * trans + b'\x00')
    data += chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    open(path, 'wb').write(data)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('res'); ap.add_argument('out')
    ap.add_argument('--palette', type=int, default=0)
    ap.add_argument('--colors', required=True, help="the game's own COLORS file")
    ap.add_argument('--max', type=int, default=0, help='max entries to export (0 = all)')
    a = ap.parse_args()
    cd = open(a.colors, 'rb').read()
    pal = cd[1 + 768 * a.palette: 1 + 768 * (a.palette + 1)]
    d = open(a.res, 'rb').read()
    n = struct.unpack_from('<I', d, 0)[0]
    offs = list(struct.unpack_from(f'<{n}I', d, 4)) + [len(d)]
    os.makedirs(a.out, exist_ok=True)
    done = skipped = 0
    for i in range(n):
        e0, e1 = offs[i], offs[i + 1]
        if e1 - e0 < 13 or d[e0:e0 + 2] != b'EH' or d[e0 + 2] != 1:
            continue
        hl = struct.unpack_from('<H', d, e0 + 3)[0]
        cnt, h, wb, tr, comp, fl = struct.unpack_from('<hhBBBB', d, e0 + 5)
        w = wb * 8
        p = e0 + 5 + hl
        try:
            if comp == 0:
                stream = d[p:p + cnt * w * h]
            elif comp == 1:
                stream, _ = rle(d, p, cnt * w * h)
            elif comp == 3:
                stream, _ = lzss(d, p, cnt * w * h, 0xFE)
            else:
                print(f'entry {i}: compression {comp} unsupported'); skipped += 1; continue
        except IndexError:
            print(f'entry {i}: stream ran out (cnt={cnt} {w}x{h} comp={comp})'); skipped += 1; continue
        t = None if tr == 0xFF else tr
        if h == 1 and cnt > 1:  # large image stored as cnt rows of height 1
            png(os.path.join(a.out, f'{i:04d}_img_{w}x{cnt}.png'), w, cnt, stream, pal, t)
            done += 1
            if a.max and done >= a.max:
                break
            continue
        for s in range(cnt):
            png(os.path.join(a.out, f'{i:04d}_{s:03d}_{w}x{h}.png'), w, h,
                stream[s * w * h:(s + 1) * w * h], pal, t)
        done += 1
        if a.max and done >= a.max:
            break
    print(f'exported {done} sprite resources, skipped {skipped}, into {a.out}')

if __name__ == '__main__':
    main()
