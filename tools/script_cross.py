#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Disassemble the dialogue scripts of all three DreamForge games with each game's own opcode table (structure only, no text).

usage: script_cross.py check GAME=RES9 [...]       validate operand layouts against all scripts
       script_cross.py table GAME=RES9 [...]       opcode by opcode comparison (Markdown)
GAME: strahd, menzo or prophet; RES9 = path of that file (Strahd and Menzoberranzan have it only on the disc).

Layouts: docs/formats/scripts.md.
"""
import struct, sys, collections

# layout: number of operands, or 'n' = count + count words, 'pairs' = speech + (test, text) pairs to the end test,
# 'choice' = target, id + up to 4 (test, text) pairs, 'remark' = flag + up to 4 pairs. Second value = index of the jump target.
COMMON = {3: (2, 0), 5: (1, None), 6: (1, None), 7: (0, None), 8: (2, 1), 9: (2, 1), 10: (1, None), 11: (1, None), 12: (1, None),
          13: (2, 1), 14: (0, None), 15: (3, 1), 16: (3, None), 17: (0, None), 18: (1, 0), 20: (1, None), 21: (0, None),
          22: (1, None), 23: (1, None), 24: (1, 0), 25: (1, None), 27: (0, None), 28: (1, None), 29: (1, None), 30: (1, None),
          31: (1, None), 32: (1, None), 33: (2, 1), 34: (2, 1), 35: (4, None), 36: (1, None), 37: (3, None), 38: (3, None),
          39: (1, None), 40: (1, None), 41: (1, None)}
STRAHD = dict(COMMON); STRAHD.update({0: ('n', None), 1: ('n', 'all'), 2: ('pairs', None), 4: (2, None), 19: (2, 1), 26: (2, 1)})
MENZO = dict(COMMON); MENZO.update({2: (2, None), 19: ('choice', 0), 26: ('choice', 0), 42: ('remark', None), 43: (3, None), 44: (0, None)})
PROPHET = dict(MENZO); PROPHET.update({4: (0, None), 45: ('n', None), 46: (3, 2), 47: (3, 2), 48: (1, None), 49: (1, None), 50: (1, None),
                                       51: (1, 0), 52: (1, 0), 53: (1, 0), 54: (2, 1), 55: ('n', None)})
DIALECT = {'strahd': (STRAHD, 0x21), 'menzo': (MENZO, 0x21), 'prophet': (PROPHET, 0x26)}
MEAN = {0: 'set all choice texts', 1: 'set all choice targets', 2: 'NPC speaks', 3: "change a choice's target", 4: "change a choice's text", 5: 'built-in character joins',
        6: 'give item', 7: 'end of branch', 8: 'jump if item', 9: 'jump if flag', 10: 'set flag', 11: 'clear flag', 12: 'toggle flag', 13: 'jump if not flag',
        14: 'open choices', 15: 'timer', 16: 'draw', 17: 'show', 18: 'call', 19: 'add choice', 20: 'remove choice', 21: 'leave after this branch', 22: 'NPC leaves',
        23: 'picture base', 24: 'jump', 25: 'text base', 26: 'add choice first', 27: 'end', 28: 'sound', 29: 'music', 30: 'set local', 31: 'clear local', 32: 'toggle local',
        33: 'jump if local', 34: 'jump if not local', 35: 'draw random', 36: 'cinematic', 37: 'draw modified', 38: 'draw clipped', 39: 'speech base', 40: 'take item',
        41: 'drop item', 42: 'party remark', 43: 'pop-up line', 44: 'inside-call marker', 45: 'fill a table of picture ids (draws of these are skipped)', 46: 'jump if an item is in a given slot', 47: 'jump if the party has exactly N of an item',
        48: 'set a numbered flag once, lower a counter', 49: 'set a numbered flag once, raise that counter', 50: 'built-in character leaves', 51: 'jump if that counter is high enough',
        52: 'jump if any of a block of flags', 53: 'jump on five flag pairs', 54: 'jump if a variable equals n', 55: 'preload pictures'}


def res(p):
    d = open(p, 'rb').read(); n, = struct.unpack_from('<I', d, 0); o = list(struct.unpack_from('<%dI' % n, d, 4)) + [len(d)]
    return [d[o[i]:o[i + 1]] for i in range(n)]


def scripts(p):
    for i, b in enumerate(res(p)):
        if len(b) > 4 and b[:2] != b'EH':
            n, = struct.unpack_from('<H', b, 0); yield i, struct.unpack_from('<%dH' % n, b, 2)


def dis(w, T, end):
    p = 0; out = []
    while p < len(w):
        op = w[p]
        if op not in T: raise ValueError('opcode %d at %d' % (op, p))
        k, tg = T[op]
        if k == 'n': L = 2 + w[p + 1]
        elif k == 'pairs':
            q = p + 2
            while w[q] != end: q += 2
            L = q + 2 - p
        elif k in ('choice', 'remark'):
            q = p + (3 if k == 'choice' else 2)
            for _ in range(4):
                x = w[q]; q += 2
                if x == end: break
            L = q - p
        else: L = 1 + k
        if p + L > len(w): raise ValueError('runs past the end at %d' % p)
        a = w[p + 1:p + L]; out.append((p, op, a, list(a[1:]) if tg == 'all' else [a[tg]] if tg is not None else [])); p += L
    return out


def use(T, end, f):
    c = collections.Counter(); r = collections.Counter(); bad = []
    for i, w in scripts(f):
        r['scripts'] += 1
        try: o = dis(w, T, end)
        except (ValueError, IndexError) as e: bad.append((i, str(e))); continue
        r['exact'] += 1; r['words'] += len(w); r['ins'] += len(o); B = {x[0] for x in o}
        for p, op, a, t in o:
            c[op] += 1
            for x in t:
                if x != 0xffff: r['targets'] += 1; r['on a boundary'] += x in B
    return c, r, bad


if __name__ == '__main__':
    if len(sys.argv) < 3: sys.exit(__doc__)
    GAMES = [(n, *DIALECT[n], f) for n, f in (a.split('=', 1) for a in sys.argv[2:])]
    if sys.argv[1] == 'check':
        for g, T, end, f in GAMES:
            c, r, bad = use(T, end, f); print(g, dict(r), 'failed:', bad); print('   %d opcodes implemented, %d used; unused %s' % (len(T), len(c), sorted(set(T) - set(c))))
        print('wrong-table control (scripts that still parse exactly):')
        for g, _, e0, f in GAMES: print('  ', g, {h: use(T, e0, f)[1]['exact'] for h, T, _, _ in GAMES})
    else:
        U = [use(T, e, f)[0] for _, T, e, f in GAMES]
        print('| Op | Meaning | ' + ' | '.join(g[0] for g in GAMES) + ' |\n|---|---|' + '---|' * len(GAMES))
        for op in range(56):
            cell = []
            for (g, T, e, f), u in zip(GAMES, U): cell.append('–' if op not in T else '%s (%s)' % ('×%d' % u[op] if u[op] else 'unused', T[op][0] if isinstance(T[op][0], str) else '%d w' % T[op][0]))
            print('| %d | %s | %s |' % (op, MEAN.get(op, ''), ' | '.join(cell)))
