#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Parse and decode DreamForge .DFA animations (Menzoberranzan; also present on the Stone Prophet CD).

usage: dfa_dump.py info FILE...                 header, frame and chunk counts
       dfa_dump.py check FILE...                decode everything; every chunk must consume exactly its bytes
       dfa_dump.py frames FILE OUTDIR [N,N,...] write frames as indexed PNGs (default: all)
       dfa_dump.py raw FILE OUT [MAXFRAMES]     all frames as raw RGB24 (6-bit VGA palette expanded), for comparisons

Format (also decoded by FFmpeg as "Chronomaster DFA"; this is an independent implementation, see NOTICE):
  header 128 bytes: "DFIA", u8 0, u8 1, u16 frames, u16 width, u16 height, u32 ms per frame, u32 offset of first chunk
  chunk: char name[4], u32 size, u32 type, size bytes. Type 0 ends a frame (size 0).
  The frame buffer is four VGA planes, each width/4 x height bytes, stored one after another (Mode X).
  All decoders draw over the previous frame (the buffer is cleared only at the start and when looping).
"""
import struct, sys, os, zlib, collections

NAMES = {0: 'EOFR', 1: 'PALT', 2: 'COPY', 3: 'TSW1', 4: 'BDLT', 5: 'WDLT', 6: 'TDLT', 7: 'DSW1', 8: 'BLCK'}


class Bad(Exception): pass


def d_copy(d, buf, w):
    buf[:len(d)] = d
    return len(d)


def _match(buf, q, v):
    n = ((v >> 13) + 2) * 2; s = q - (v & 0x1fff) * 2
    if s < 0: raise Bad('match before buffer')
    if s == q: return q + n                       # offset 0 copies onto itself
    if q + n > len(buf): raise Bad('match past buffer')
    if s + n <= q: buf[q:q + n] = buf[s:s + n]
    else:                                         # overlapping: repeats with period q-s
        per = bytes(buf[s:q]); buf[q:q + n] = (per * (n // len(per) + 1))[:n]
    return q + n


def d_tsw1(d, buf, w):
    """LZ on 16-bit words: u32 tokens, u32 start offset, control words (bit 1 = match), 13-bit word offset, 3-bit length - 2"""
    n, q = struct.unpack_from('<II', d, 0); p = 8; U = memoryview(d)
    ctl = 0; bits = 0
    for _ in range(n):
        if not bits: ctl = d[p] | d[p + 1] << 8; p += 2; bits = 16
        if ctl & 1: q = _match(buf, q, d[p] | d[p + 1] << 8)
        else:
            buf[q:q + 2] = U[p:p + 2]; q += 2
        p += 2; ctl >>= 1; bits -= 1
    if len(buf) != w[2]: raise Bad('wrote past the frame')
    return p


def d_dsw1(d, buf, w):
    """as TSW1 with 2-bit tokens: x1 = match, 00 = literal word, 10 = skip u16 bytes"""
    n, = struct.unpack_from('<H', d, 0); p = 2; q = 0; ctl = bits = 0
    for _ in range(n):
        if not bits: ctl = d[p] | d[p + 1] << 8; p += 2; bits = 8
        v = d[p] | d[p + 1] << 8
        if ctl & 1: q = _match(buf, q, v)
        elif ctl & 2: q += v
        else: buf[q:q + 2] = d[p:p + 2]; q += 2
        p += 2; ctl >>= 2; bits -= 1
    if len(buf) != w[2]: raise Bad('wrote past the frame')
    return p


def d_bdlt(d, buf, w):
    """byte delta: u16 first line, u16 lines; per line u8 segments of {u8 skip, s8 n: n>0 literal bytes, n<0 run of -n}"""
    y, n = struct.unpack_from('<HH', d, 0); p = 4
    for line in range(n):
        q = (y + line) * w[0]; seg = d[p]; p += 1
        for _ in range(seg):
            q += d[p]; c = d[p + 1]; p += 2
            if c < 128: buf[q:q + c] = d[p:p + c]; p += c
            else: c = 256 - c; buf[q:q + c] = d[p:p + 1] * c; p += 1
            q += c
    if len(buf) != w[2]: raise Bad('wrote past the frame')
    return p


def d_wdlt(d, buf, w):
    """word delta: u16 lines; per line an optional s16 prefix (0xC000+ = skip -n lines, 0x8000+ = last byte of the line),
    then u16 segments of {u8 skip bytes, s8 n: n>=0 literal words, n<0 one word repeated -n times}"""
    n, = struct.unpack_from('<H', d, 0); p = 2; base = 0
    for _ in range(n):
        while True:
            a = d[p] | d[p + 1] << 8; p += 2
            if a >= 0xc000: base += (0x10000 - a) * w[0]; continue
            break
        if a & 0x8000:
            buf[base + w[0] - 1] = a & 0xff
            a = d[p] | d[p + 1] << 8; p += 2
            if a & 0x8000: raise Bad('two prefixes')
        q = base
        for _ in range(a):
            q += d[p]; c = d[p + 1]; p += 2
            if c < 128: c *= 2; buf[q:q + c] = d[p:p + c]; p += c
            else: c = (256 - c) * 2; buf[q:q + c] = d[p:p + 2] * (c // 2); p += 2
            q += c
        base += w[0]
    if len(buf) != w[2]: raise Bad('wrote past the frame')
    return p


def d_tdlt(d, buf, w):
    """token delta: u32 tokens of {u8 words to copy, u8 words to skip first}"""
    n, = struct.unpack_from('<I', d, 0); p = 4; q = 0
    for _ in range(n):
        c = d[p] * 2; q += d[p + 1] * 2; p += 2
        if c: buf[q:q + c] = d[p:p + c]; p += c; q += c
    if len(buf) != w[2]: raise Bad('wrote past the frame')
    return p




def header(b):
    m, v0, v1, fr, w, h, ms, off = struct.unpack_from('<4sBBHHHII', b, 0)
    if m != b'DFIA': raise Bad('not a DFA file')
    return dict(v=(v0, v1), frames=fr, w=w, h=h, ms=ms, first=off, rest_zero=not any(b[20:128]))


def chunks(f, H):
    f.seek(H['first'])
    while True:
        h = f.read(12)
        if len(h) < 12:
            if h: raise Bad('%d stray bytes at the end' % len(h))
            return
        nm, size, t = struct.unpack('<4sII', h); d = f.read(size)
        if len(d) != size: raise Bad('chunk runs past the end')
        yield nm.decode('latin1'), size, t, d


def d_blck(d, buf, w):
    """clear the frame (Stone Prophet only; Menzoberranzan's player has no type 8)"""
    buf[:] = bytes(len(buf))
    return len(d)


DEC = {8: d_blck, 2: d_copy, 3: d_tsw1, 4: d_bdlt, 5: d_wdlt, 6: d_tdlt, 7: d_dsw1}


def deplane(buf, H):
    """the buffer holds four VGA Mode X planes one after another; make it one byte per pixel, left to right"""
    if H['v'][1] != 1: return bytes(buf)
    n = len(buf) // 4; out = bytearray(len(buf))
    for p in range(4): out[p::4] = buf[p * n:(p + 1) * n]
    return bytes(out)


def play(path, stats=None, slack=None):
    """yield (frame number, palette bytes, planar frame buffer, header) after each frame; use deplane() for the picture"""
    with open(path, 'rb') as f:
        H = header(f.read(128)); wh = H['w'] * H['h']; buf = bytearray(wh); pal = bytes(768); n = 0; ctx = (H['w'], H['h'], wh)
        for nm, size, t, d in chunks(f, H):
            if stats is not None: stats[nm, t] += 1; stats['bytes ' + nm] += size
            if t == 0: yield n, pal, buf, H; n += 1; continue
            if t == 1: pal = d; used = 768
            elif t in DEC: used = DEC[t](d, buf, ctx)
            else: raise Bad('unknown chunk type %d %s' % (t, nm))
            if slack is not None: slack[nm, len(d) - used] += 1
        H['played'] = n


def png(path, w, h, pal6, px):
    ch = lambda t, c: struct.pack('>I', len(c)) + t + c + struct.pack('>I', zlib.crc32(t + c))
    pal = bytes((v & 63) << 2 | (v & 63) >> 4 for v in pal6)
    raw = b''.join(b'\0' + bytes(px[y * w:(y + 1) * w]) for y in range(h))
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + ch(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 3, 0, 0, 0)) + ch(b'PLTE', pal) + ch(b'IDAT', zlib.compress(raw, 6)) + ch(b'IEND', b''))


if __name__ == '__main__':
    cmd, a = sys.argv[1], sys.argv[2:]
    if cmd == 'info':
        for p in a:
            with open(p, 'rb') as f:
                H = header(f.read(128)); c = collections.Counter(nm for nm, *_ in chunks(f, H)); print(os.path.basename(p), H, dict(c))
    elif cmd == 'check':
        tot = collections.Counter(); TS = collections.Counter(); good = 0
        for p in a:
            st = collections.Counter(); sl = collections.Counter(); n = 0; H = None
            try:
                for n0, pal, buf, H in play(p, st, sl): n += 1
                ok = n == H['frames']; msg = '' if ok else 'frames %d of %d' % (n, H['frames'])
            except (Bad, IndexError, struct.error) as e: ok = False; msg = repr(e)
            good += ok; TS += sl; tot += st
            print('%-13s %4d frames %s' % (os.path.basename(p), n, 'ok' if ok else 'FAILED ' + msg), {k[0] if isinstance(k, tuple) else k: v for k, v in st.items() if isinstance(k, tuple)}, 'slack', {k: v for k, v in sl.items() if k[1]}, flush=True)
        print('files ok', good, 'of', len(a)); print('chunks', {k: v for k, v in tot.items() if isinstance(k, tuple)}); print('unused bytes per chunk', dict(TS))
    elif cmd == 'frames':
        want = {int(x) for x in a[2].split(',')} if len(a) > 2 else None; os.makedirs(a[1], exist_ok=True)
        for n, pal, buf, H in play(a[0]):
            if want is None or n in want: png(os.path.join(a[1], 'f%04d.png' % n), H['w'], H['h'], pal, deplane(buf, H))
    elif cmd == 'raw':
        lim = int(a[2]) if len(a) > 2 else 10**9
        with open(a[1], 'wb') as o:
            for n, pal, buf, H in play(a[0]):
                if n >= lim: break
                P = [bytes((pal[3 * i + k] & 63) << 2 | (pal[3 * i + k] & 63) >> 4 for k in range(3)) for i in range(256)]
                o.write(b''.join(P[v] for v in deplane(buf, H)))
