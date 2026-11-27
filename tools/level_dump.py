#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Parse Menzoberranzan LEVELS.DAT / saved LEVnnn.DAT and check every cross-reference.

usage: level_dump.py GAMEDIR check            validate all 56 levels (summary)
       level_dump.py GAMEDIR level N          print one level's spawns, records and trigger lists
       level_dump.py GAMEDIR map N            print one level's header, ASCII map, wall types and records
                                             (same text as the Unity LevelMapLogger, for diffing)
       level_dump.py GAMEDIR textures         resolve every real level's wall faces and tiles through INIT
                                             (same text as the Unity TextureResolutionLogger, for diffing)
       level_dump.py GAMEDIR savelev FILE     parse a saved LEVnnn.DAT

Layouts: docs/formats/levels.md.
The per-entry section table is NOT trusted: the engine reads the sections in sequence, so this does too.
"""
import struct, sys, os, collections

OPS = {0: 'open/enable', 1: 'close/disable', 2: 'toggle', 3: 'fire_effect', 4: 'set_wall', 5: 'sound', 6: 'music',
       7: 'show_text', 8: 'flag_set', 9: 'flag_clear', 10: 'flag_toggle', 11: 'cinematic', 12: 'event', 13: 'spawn_npc'}
KINDS = {0: 'decal', 1: 'door', 2: 'wall_switch', 3: 'floor_trigger', 4: 'lock', 6: 'spinner', 8: 'exit', 10: 'examine', 11: 'used'}


class R:
    def __init__(s, b, p=0): s.b, s.p = b, p
    def take(s, n):
        if s.p + n > len(s.b): raise ValueError('past end at 0x%x+%d of %d' % (s.p, n, len(s.b)))
        r = s.b[s.p:s.p + n]; s.p += n; return r
    def u(s, f): return struct.unpack('<' + f, s.take(struct.calcsize('<' + f)))


def record(q):
    k, f, trig, trig2, y, x, a6, a7 = struct.unpack_from('<BBbbBBBB', q, 0)
    return dict(kind=k, flags=f, trig=trig, trig2=trig2, y=y, x=x, a6=a6, a7=a7, tex=struct.unpack_from('<h', q, 8)[0], raw=q)


def spawn(q):
    t, cx, fx, cy, fy, fac = struct.unpack('<hhHhHB', q)
    return dict(type=t, x=cx, y=cy, fx=fx, fy=fy, facing=fac)


def header(h):
    w, hh = struct.unpack_from('<hh', h, 1)
    npc, item, obj = struct.unpack_from('<3h', h, 0x80a)
    return dict(tag=h[0], w=w, h=hh, ceiling=struct.unpack_from('<h', h, 5)[0], floor=struct.unpack_from('<h', h, 7)[0], one_story=h[9],
                npcs=npc, items=item, objects=obj, sky=h[0x810], wallset=h[0x811], ceil_colour=h[0x818], floor_colour=h[0x819],
                light=h[0x81a], music=h[0x81c],
                walls=[struct.unpack_from('<16h', h, 10 + 32 * i) for i in range(64)])


def level(b, saved=False):
    r = R(b); L = {}
    L['zero'] = r.take(1)[0]; L['sect'] = r.u('7I')
    r.p = 0x1d
    H = L['hdr'] = header(r.take(0x824))
    L['cells'] = r.take(H['w'] * H['h'] * 2)
    real = [0x1d, r.p]
    recs = []
    for x in range(H['w']):
        n, = r.u('h'); recs += [(x, record(r.take(16))) for _ in range(n)]
    if saved:
        L['doors_moving'] = r.take(1)[0]
        for x in range(H['w']):
            n, = r.u('h'); recs += [(x, record(r.take(16))) for _ in range(n)]
    L['recs'] = recs
    real.append(r.p)
    if not saved:
        L['npc'] = [spawn(r.take(11)) for _ in range(H['npcs'])]
        real.append(r.p)
        L['obj'] = [spawn(r.take(11)) for _ in range(H['objects'])]
        real.append(r.p)
        L['item'] = [spawn(r.take(11)) for _ in range(H['items'])]
    else:
        def ents(n, isz):
            e = [struct.unpack('<iiII', r.take(16)) for _ in range(n)]
            return list(zip(e, [r.take(isz) for _ in range(n)]))
        L['npc'] = ents(H['npcs'], 0x2e)
        real.append(r.p)
        n, = r.u('h'); L['objcount2'] = n; L['obj'] = ents(n, 0x24)
        real.append(r.p)
        e = [struct.unpack('<iiII', r.take(16)) for _ in range(H['items'])]
        L['item'] = list(zip(e, [r.take(0x24) for _ in range(H['items'])]))
    real.append(r.p); t = r.take(0x514); real.append(r.p); L['real'] = real
    L['trig'] = [[tuple(t[26 * i + 1 + 5 * k:26 * i + 6 + 5 * k]) for k in range(min(t[26 * i], 5))] for i in range(50)]
    L['trig_count_ok'] = all(t[26 * i] <= 5 or t[26 * i] == 0xff for i in range(50))
    L['dims2'] = r.u('hh')
    L['automap'] = r.take(H['w'] * H['h'])
    n, = r.u('I'); L['notes'] = r.take(n)
    L['left'] = len(b) - r.p
    return L


def levels(gd):
    d = open(os.path.join(gd, 'LEVELS.DAT'), 'rb').read()
    n, = struct.unpack_from('<I', d, 0); off = list(struct.unpack_from('<%dI' % n, d, 4)) + [len(d)]
    return [d[off[i]:off[i + 1]] for i in range(n)]


def names(gd):
    o = open(os.path.join(gd, 'OBJECTS.DAT'), 'rb').read(); p = open(os.path.join(gd, 'NPCS.DAT'), 'rb').read()
    f = lambda b, s: [b[4 + i * s:4 + (i + 1) * s][:20 if s == 0x58 else 40].split(b'\0')[0].decode('latin1') for i in range(struct.unpack_from('<I', b, 0)[0])]
    return f(o, 0x41), f(p, 0x58)


def check(gd):
    items, npcs = names(gd); nobj, = struct.unpack_from('<H', open(os.path.join(gd, 'INIT'), 'rb').read(), 0x5c)
    P = [level(b) for b in levels(gd)]
    c = collections.Counter(); bad = collections.defaultdict(list); kinds = collections.Counter(); ops = collections.Counter(); exits = []
    for i, L in enumerate(P):
        H = L['hdr']; w, h = H['w'], H['h']
        c['levels'] += 1; c['bytes left over'] += L['left']; c['levels whose section table is stale (unused by the engine)'] += L['real'] != list(L['sect'])
        c['dims repeated ok'] += L['dims2'] == (w, h)
        c['placeholder'] += (w, h) == (12, 12) and not (H['npcs'] or H['items'] or H['objects'])
        for x, q in L['recs']:
            kinds[q['kind']] += 1
            c['records'] += 1; c['record x == column'] += q['x'] == x; c['record y in map'] += q['y'] < h
            if q['kind'] in (2, 3, 4) and q['flags'] & 4:
                c['trigger refs'] += 1; ok = -1 <= q['trig'] < 50; c['trigger refs in range'] += ok
                c['trigger refs to non-empty list'] += ok and q['trig'] >= 0 and bool(L['trig'][q['trig']])
            if q['kind'] == 4:
                c['locks'] += 1; key = q['a6']; c['lock key is a key item'] += key < len(items) and 'KEY' in items[key]; bad['lock keys'].append(items[key])
            if q['kind'] == 8: exits.append((i, q))
        for L2, lim, nm in ((L['npc'], len(npcs), 'npc'), (L['obj'], nobj, 'object'), (L['item'], len(items), 'item')):
            for s in L2:
                c[nm + ' spawns'] += 1; c[nm + ' type valid'] += 0 <= s['type'] < lim and (nm != 'npc' or bool(npcs[s['type']]))
                c[nm + ' in map'] += 0 <= s['x'] < w and 0 <= s['y'] < h
                c[nm + ' facing < 8'] += s['facing'] < 8
        where = {(s['x'], s['y'], s['fx'], s['fy']) for s in L['npc']}
        c['items carried by an npc (same exact spot)'] += sum((s['x'], s['y'], s['fx'], s['fy']) in where for s in L['item'])
        for k, t in enumerate(L['trig']):
            for a in t:
                ops[a[0]] += 1; c['actions'] += 1; c['action op 0-13'] += a[0] <= 13
                if a[0] in (0, 1, 2):
                    c['open/close/toggle actions'] += 1
                    c['  ...target cell has a record'] += any(q['x'] == a[2] and q['y'] == a[1] for _, q in L['recs'])
                if a[0] == 13: c['spawn actions'] += 1; c['  ...valid npc'] += a[3] < len(npcs) and bool(npcs[a[3]])
                if a[0] in (3, 4): c['cell actions'] += 1; c['  ...cell in map'] += a[2] < w and a[1] < h
    for i, q in exits:
        dl, dx, dy, dir_ = q['a6'], q['a7'], q['raw'][8], q['raw'][9]
        c['exits'] += 1; c['exit level < 56'] += dl < len(P)
        if dl < len(P):
            D = P[dl]['hdr']; inside = dx < D['w'] and dy < D['h']
            c['exit lands inside target map'] += inside
            if inside: c['exit lands on open cell (wall id 0)'] += (P[dl]['cells'][2 * (dy * D['w'] + dx)] & 0x3f) == 0
            c['exits with a matching exit back'] += any(a == dl and b['a6'] == i for a, b in exits)
        else: bad['exit level'].append((i, dl))
    for k, v in c.items(): print('%-46s %d' % (k, v))
    print('record kinds', {KINDS.get(k, k): v for k, v in sorted(kinds.items())})
    print('action ops  ', {OPS.get(k, k): v for k, v in sorted(ops.items())})
    print('lock keys   ', collections.Counter(bad['lock keys']).most_common(12)); print('exits out of range', bad['exit level'][:10])


def show(gd, L, i):
    items, npcs = names(gd); H = L['hdr']
    print('level', i, {k: v for k, v in H.items() if k != 'walls'}, 'section table', L['sect'])
    for s in L['npc']: print('  npc ', npcs[s['type']], (s['x'], s['y']), 'facing', s['facing'], ('script %d' % (s['type'] - 0x31)) if 0x31 < s['type'] < 0x56 else '')
    for s in L['item']: print('  item', items[s['type']], (s['x'], s['y']))
    for x, q in L['recs']:
        if q['kind'] == 2 and not q['flags'] & 4: continue
        print('  rec ', KINDS.get(q['kind'], q['kind']), (q['x'], q['y']), 'flags %02x' % q['flags'], 'trig', q['trig'], q['trig2'], q['raw'][6:].hex(' '))
    for k, t in enumerate(L['trig']):
        for a in t: print('  trig', k, OPS.get(a[0], a[0]), 'cell', (a[2], a[1]), 'args', a[3:])


WALL_CHARS = '.123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ$%'
LETTERS = [(8, 'X'), (1, 'D'), (4, 'L'), (2, 'S'), (3, 'T'), (6, '@'), (10, 'E'), (11, 'u'), (0, 'd')]  # priority order


def map_text(L, i):
    """Header, ASCII map, wall-type map and records, in the Unity LevelMapLogger's format."""
    H = L['hdr']; w, h = H['w'], H['h']; c = L['cells']
    out = ['level %d: %dx%d' % (i, w, h),
           'header ceiling %d floor %d two_rows_flag %d sky %d wallset %d colours %d %d light %d music %d' % (
               H['ceiling'], H['floor'], H['one_story'], H['sky'], H['wallset'], H['ceil_colour'], H['floor_colour'], H['light'], H['music']),
           'counts npcs %d items %d objects %d records %d trigger_lists_used %d automap %d notes %d left_over %d' % (
               H['npcs'], H['items'], H['objects'], len(L['recs']), sum(1 for t in L['trig'] if t), len(L['automap']), len(L['notes']), L['left'])]
    rank = {k: n for n, (k, _) in enumerate(LETTERS)}; letter = dict(LETTERS); best = {}
    for _, q in L['recs']:
        if q['x'] < w and q['y'] < h:
            s = rank.get(q['kind'], len(LETTERS))
            if (q['x'], q['y']) not in best or s < best[q['x'], q['y']][0]:
                best[q['x'], q['y']] = (s, letter.get(q['kind'], '?'))
    out.append('map (x across, y down; . open, # wall, ~ vanishing wall, letters = records)')
    for y in range(h):
        row = ''
        for x in range(w):
            b0, b1 = c[2 * (y * w + x)], c[2 * (y * w + x) + 1]
            row += best[x, y][1] if (x, y) in best else '.' if not b0 & 0x3f else '~' if b1 & 0x08 else '#'
        out.append(row)
    out.append('wall types (. = 0, 1-9 a-z A-Z = 1-61, $ = 62, % = 63)')
    for y in range(h):
        out.append(''.join(WALL_CHARS[c[2 * (y * w + x)] & 0x3f] for x in range(w)))
    out.append('records (x y kind flags trig trig2 a6 a7 overlay click face row)')
    for _, q in L['recs']:
        r = q['raw']
        out.append('rec %d %d %d %02x %d %d %d %d %d %d,%d,%d,%d %d %d' % (
            q['x'], q['y'], q['kind'], q['flags'], q['trig'], q['trig2'], q['a6'], q['a7'], q['tex'], r[10], r[11], r[12], r[13], r[14], r[15]))
    return '\n'.join(out)


def textures_report(gd):
    """One line per real level: every half-row of every face of every used wall type resolved through
    INIT's wall-set table (docs/formats/types.md, levels.md) to a RES4 entry, classified W (type-5 wall),
    S (type-6 shape) or M (missing); plus the floor and ceiling tiles. Same text as the Unity
    TextureResolutionLogger, for diffing."""
    import hashlib
    ini = open(os.path.join(gd, 'INIT'), 'rb').read()
    tile_start, = struct.unpack_from('<H', ini, 0x16)
    table = struct.unpack_from('<15H', ini, 0x2A)
    r4 = open(os.path.join(gd, 'RES4'), 'rb').read()
    n, = struct.unpack_from('<I', r4, 0)
    offs = struct.unpack_from('<%dI' % n, r4, 4)
    def kind(e):
        if not 0 <= e < n or r4[offs[e]:offs[e] + 2] != b'EH': return 'M'
        t = r4[offs[e] + 2]
        if t == 5: return {12290: 'W', 4098: 'T'}.get(struct.unpack_from('<I', r4, offs[e] + 6)[0], 'M')
        return 'S' if t == 6 else 'M'
    out = []
    all_levels = levels(gd)
    copies = collections.Counter(all_levels)
    for i, raw in enumerate(all_levels):
        if copies[raw] > 1:  # placeholders: identical copies of one level (21 in M); real 12 x 12 levels exist
            continue
        L = level(raw); H = L['hdr']
        ws = H['wallset']; rows = 2 if H['one_story'] == 0 else 1
        start, end = (table[ws], table[ws + 1]) if ws < 14 else (None, None)
        used = sorted({c & 0x3f for c in L['cells'][0::2]} - {0})
        lines = []; cnt = collections.Counter()
        for t in used:
            for f in range(4):
                for r in range(rows):
                    for h in range(2):
                        num = H['walls'][t][4 * f + 2 * r + h]
                        if start is None:
                            e, k = -1, 'M'
                        else:
                            e = start + num
                            k = kind(e) if 0 <= num and e < end else 'M'
                            k = k if k in 'WS' else 'M'
                        cnt[k] += 1
                        lines.append('%d %d %d %d %d %d %s\n' % (t, f, r, h, num, e, k))
        def tile(m):
            if m == -1: return 'none'
            return str(tile_start + m) if m >= 0 and kind(tile_start + m) == 'T' else '?'
        digest = hashlib.sha256(''.join(lines).encode('ascii')).hexdigest()[:16]
        out.append('%2d set %2d [%d,%d) rows %d types %2d: halves %4d walls %4d shapes %4d missing %d | floor %d->%s ceiling %d->%s | %s' % (
            i, ws, start, end, rows, len(used), sum(cnt.values()), cnt['W'], cnt['S'], cnt['M'],
            H['floor'], tile(H['floor']), H['ceiling'], tile(H['ceiling']), digest))
    return '\n'.join(out)

if __name__ == '__main__':
    gd, cmd = sys.argv[1], sys.argv[2]
    if cmd == 'check': check(gd)
    elif cmd == 'level': show(gd, level(levels(gd)[int(sys.argv[3])]), int(sys.argv[3]))
    elif cmd == 'map': print(map_text(level(levels(gd)[int(sys.argv[3])]), int(sys.argv[3])))
    elif cmd == 'textures': print(textures_report(gd))
    elif cmd == 'savelev':
        L = level(open(sys.argv[3], 'rb').read(), True)
        print({k: v for k, v in L['hdr'].items() if k != 'walls'}); print('section table matches', L['real'] == list(L['sect']), 'records', len(L['recs']), 'npcs', len(L['npc']), 'objects', len(L['obj']), L['objcount2'], 'items', len(L['item']), 'dims', L['dims2'], 'notes', len(L['notes']), 'left over', L['left'], 'sect', L['sect'])
