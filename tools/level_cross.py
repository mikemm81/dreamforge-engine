#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Parse LEVELS.DAT of all three DreamForge games with one parser and per-game sizes; every level must end exactly.

usage: level_cross.py GAME=DIR [GAME=DIR ...]     GAME: strahd, menzo or prophet; DIR holds LEVELS.DAT, NPCS.DAT, OBJECTS.DAT

Layouts: docs/formats/levels.md.
"""
import struct, sys, os, collections

G = {'strahd': dict(hdr=0x2a4, rec=10, cnt=0x28a, npcsz=0x45),
     'menzo': dict(hdr=0x824, rec=16, cnt=0x80a, npcsz=0x58),
     'prophet': dict(hdr=0x824, rec=16, cnt=0x80a, npcsz=0x58)}


def count(f): return struct.unpack_from('<I', open(f, 'rb').read(4), 0)[0]


def run(levels, g, types):
    c = collections.Counter(); kinds = collections.Counter(); ops = collections.Counter()
    for b in levels:
        c['levels'] += 1
        try:
            h = b[0x1d:0x1d + g['hdr']]; w, hh = struct.unpack_from('<hh', h, 1); np, it, ob = struct.unpack_from('<3h', h, g['cnt']); p = 0x1d + g['hdr'] + 2 * w * hh
            R = []; S = []
            for x in range(w):
                n, = struct.unpack_from('<h', b, p); p += 2
                for _ in range(n): R.append((x, b[p:p + g['rec']])); p += g['rec']
            for k, n in (('npc', np), ('object', ob), ('item', it)):
                for _ in range(n): S.append((k, struct.unpack_from('<hhHhHB', b, p))); p += 11
            t = b[p:p + 0x514]; p += 0x514; d = struct.unpack_from('<hh', b, p); p += 4 + w * hh; n, = struct.unpack_from('<I', b, p); p += 4 + n
        except struct.error: c['ran past the end'] += 1; continue
        if p != len(b): c['wrong length'] += 1; continue
        c['end exactly'] += 1; c['size repeated ok'] += d == (w, hh)
        for x, q in R: kinds[q[0]] += 1; c['records'] += 1; c['  record x == its column'] += q[5] == x; c['  record y in map'] += q[4] < hh
        for k, s in S:
            c[k + ' spawns'] += 1; c['  in map, facing < 8'] += 0 <= s[1] < w and 0 <= s[3] < hh and s[5] < 8
            if k != 'object': c['  type exists'] += 0 <= s[0] < types[k]
        for i in range(50):
            m = t[26 * i]; c['trigger lists'] += 1; c['  count 0-5'] += m <= 5
            for a in range(min(m, 5)): ops[t[26 * i + 1 + 5 * a]] += 1
    return c, kinds, ops


if __name__ == '__main__':
    if len(sys.argv) < 2: sys.exit(__doc__)
    for a in sys.argv[1:]:
        n, gd = a.split('=', 1); g = G[n]; f = lambda x: os.path.join(gd, x)
        d = open(f('LEVELS.DAT'), 'rb').read(); k, = struct.unpack_from('<I', d, 0); o = list(struct.unpack_from('<%dI' % k, d, 4)) + [len(d)]
        L = [d[o[i]:o[i + 1]] for i in range(k)]; ty = {'npc': count(f('NPCS.DAT')), 'item': count(f('OBJECTS.DAT'))}
        c, kinds, ops = run(L, g, ty); print(n, dict(c)); print('   record kinds', dict(sorted(kinds.items())), '\n   trigger ops', dict(sorted(ops.items())))
        for m, h in G.items():
            if m != n: print('   with %s sizes: %s' % (m, {q: v for q, v in run(L, h, ty)[0].items() if q in ('end exactly', 'ran past the end', 'wrong length')}))
