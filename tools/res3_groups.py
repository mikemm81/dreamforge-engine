#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Group exported RES3 column shapes into creatures / objects / items with frame+view names.

Index formula:
    shape = kind_base[kind] + type_base + frame * views + view
  kind_base: INIT u16 at 0x0E (NPC=25), 0x10 (object=1946), 0x12 (item=2726), 0x14 (decoration=2976)
  NPC   : NPCS.DAT  (u32 n, n x 0x58)  base +0x14, views byte +0x1a, walk frames byte +0x1b
  object: INIT @0x5C (u16 n, n x 10)   base +0,    views byte +4
  item  : OBJECTS.DAT (u32 n, n x 0x41) base +0x28, views byte +0x2a
  Shapes 0..24 (before the NPC base) are shared effects; decoration types are not in INIT and are
  listed one per shape. A group spans from its base to the next known base in the same kind.

usage: res3_groups.py SHAPES_DIR OUTDIR --gamedir DIR [--names CSV]
  --names: your own names for object types (the data has none); keep the file out of the repository
  SHAPES_DIR = output of res_shapes.py RES3 (full-canvas PNGs named NNNN_WxH.png)
Writes OUTDIR/<kind>/<base>_<name>/fFF_vV_NNNN.png (copies), OUTDIR/manifest.csv and a
contact sheet OUTDIR/<kind>/<base>_<name>.png per group (rows = frames, columns = views).
"""
import os, re, struct, sys, zlib, shutil, argparse, csv

def read_png_rgba(p):
    b = open(p, 'rb').read(); pos = 8; idat = b''
    while pos < len(b):
        l, = struct.unpack('>I', b[pos:pos + 4]); t = b[pos + 4:pos + 8]; c = b[pos + 8:pos + 8 + l]; pos += 12 + l
        if t == b'IHDR': w, h = struct.unpack('>II', c[:8])
        elif t == b'IDAT': idat += c
    raw = zlib.decompress(idat)
    return w, h, [raw[y * (w * 4 + 1) + 1:(y + 1) * (w * 4 + 1)] for y in range(h)]

def write_png_rgb(path, w, h, rows):
    def chunk(t, c):
        return struct.pack('>I', len(c)) + t + c + struct.pack('>I', zlib.crc32(t + c) & 0xffffffff)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(b''.join(b'\0' + bytes(r) for r in rows), 9))
                           + chunk(b'IEND', b''))

def slug(s):
    return re.sub(r'[^A-Za-z0-9]+', '_', s).strip('_').lower() or 'unnamed'

def load_defs_strahd(gd, init, npcs):
    """Ravenloft: Strahd's Possession (older engine, detected by 69-byte NPCS.DAT records).
    INIT: kind bases u16 at 0x06 (NPC=0), 0x08 (object), 0x0A (item), 0x0C (decoration);
          object table u16 n at 0x24, then n x 7 bytes: u16 base, u16 flags, u8 views, u8 frames, u8 ?
          (views 16 = 16 directions drawn with 8 shapes).
    NPCS.DAT: u32 n, n x 0x45: name +0, base u16 +0x14, views byte +0x18 (8 monsters), walk frames +0x19.
    OBJECTS.DAT: same as Menzo (0x41 records, base +0x28, views +0x2a)."""
    kb = dict(npc=struct.unpack_from('<h', init, 0x06)[0], object=struct.unpack_from('<h', init, 0x08)[0],
              item=struct.unpack_from('<h', init, 0x0A)[0], decor=struct.unpack_from('<h', init, 0x0C)[0])
    groups = {}
    def add(kind, base, name, views, frames=None):
        g = groups.setdefault(base, dict(kind=kind, names=[], views=max(1, views), frames=frames))
        if name and name not in g['names']:
            g['names'].append(name)
    for i in range(struct.unpack_from('<I', npcs, 0)[0]):
        r = npcs[4 + i * 0x45:4 + (i + 1) * 0x45]
        name = r[:r.index(0)].decode('latin1')
        if name and r[0x18]:
            add('npc', kb['npc'] + struct.unpack_from('<H', r, 0x14)[0], name, r[0x18], r[0x19] or None)
    n = struct.unpack_from('<H', init, 0x24)[0]
    for i in range(n):
        base, _, views, frames, _ = struct.unpack_from('<HHBBB', init, 0x26 + 7 * i)
        if views:
            add('object', kb['object'] + base, f'type{i:03d}', 8 if views == 16 else views)
    d = open(os.path.join(gd, 'OBJECTS.DAT'), 'rb').read()
    for i in range(struct.unpack_from('<I', d, 0)[0]):
        r = d[4 + i * 0x41:4 + (i + 1) * 0x41]
        name = r[:r.index(0)].decode('latin1') if 0 in r else ''
        if name and r[0x2a]:
            add('item', kb['item'] + struct.unpack_from('<H', r, 0x28)[0], name, r[0x2a])
    return kb, groups

def load_defs(gd):
    init = open(os.path.join(gd, 'INIT'), 'rb').read()
    npcs = open(os.path.join(gd, 'NPCS.DAT'), 'rb').read()
    n = struct.unpack_from('<I', npcs, 0)[0]
    if n and (len(npcs) - 4) == n * 0x45:
        return load_defs_strahd(gd, init, npcs)
    kb = dict(npc=struct.unpack_from('<h', init, 0x0E)[0], object=struct.unpack_from('<h', init, 0x10)[0],
              item=struct.unpack_from('<h', init, 0x12)[0], decor=struct.unpack_from('<h', init, 0x14)[0])
    groups = {}  # absolute base -> dict(kind, names, views, walk_frames)
    def add(kind, base, name, views, frames=None):
        g = groups.setdefault(base, dict(kind=kind, names=[], views=max(1, views), frames=frames))
        if name and name not in g['names']:
            g['names'].append(name)
    d = open(os.path.join(gd, 'NPCS.DAT'), 'rb').read()
    for i in range(struct.unpack_from('<I', d, 0)[0]):
        r = d[4 + i * 0x58:4 + (i + 1) * 0x58]
        name = r[:r.index(0)].decode('latin1')
        base = struct.unpack_from('<H', r, 0x14)[0]
        if name and r[0x1a]:
            add('npc', kb['npc'] + base, name, r[0x1a], r[0x1b])
    n = struct.unpack_from('<H', init, 0x5C)[0]
    for i in range(n):
        r = init[0x5E + i * 10:0x5E + (i + 1) * 10]
        base, views = struct.unpack_from('<H', r, 0)[0], r[4]
        if views:
            add('object', kb['object'] + base, f'type{i:03d}', views)
    d = open(os.path.join(gd, 'OBJECTS.DAT'), 'rb').read()
    for i in range(struct.unpack_from('<I', d, 0)[0]):
        r = d[4 + i * 0x41:4 + (i + 1) * 0x41]
        name = r[:r.index(0)].decode('latin1') if 0 in r else ''
        if name and r[0x2a]:
            add('item', kb['item'] + struct.unpack_from('<H', r, 0x28)[0], name, r[0x2a])
    return kb, groups

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('shapes'); ap.add_argument('out')
    ap.add_argument('--gamedir', required=True, help='directory with INIT, NPCS.DAT and OBJECTS.DAT')
    ap.add_argument('--names', help='CSV kind,base,name of hand-assigned names (base = absolute RES3 index)')
    a = ap.parse_args()
    kb, groups = load_defs(a.gamedir)
    if a.names:
        with open(a.names, newline='') as f:
            for r in csv.DictReader(f):
                b = int(r['base'])
                if b in groups and groups[b]['kind'] == r['kind']:
                    groups[b]['names'].insert(0, r['name'])
                else:
                    print(f"names: no {r['kind']} group at {b}")
    files = {}
    for f in os.listdir(a.shapes):
        m = re.match(r'(\d{4})_\d+x\d+\.png$', f)
        if m:
            files[int(m.group(1))] = os.path.join(a.shapes, f)
    top = max(files) + 1
    # kind boundaries: effects < npc base <= npc < object base <= object < item <= item < decor
    bounds = [('effect', 0, kb['npc']), ('npc', kb['npc'], kb['object']), ('object', kb['object'], kb['item']),
              ('item', kb['item'], kb['decor']), ('decor', kb['decor'], top)]
    os.makedirs(a.out, exist_ok=True)
    rows = []
    for kind, lo, hi in bounds:
        bases = sorted(b for b, g in groups.items() if g['kind'] == kind and lo <= b < hi)
        if kind in ('effect', 'decor'):
            spans = [(i, i + 1, dict(names=[f'{kind}{i:04d}'], views=1, frames=None)) for i in range(lo, hi)]
        else:
            spans = [(b, (bases[k + 1] if k + 1 < len(bases) else hi), groups[b]) for k, b in enumerate(bases)]
            if bases and bases[0] > lo:
                spans.insert(0, (lo, bases[0], dict(names=['unreferenced'], views=1, frames=None)))
        kdir = os.path.join(a.out, kind)
        os.makedirs(kdir, exist_ok=True)
        for b0, b1, g in spans:
            present = [i for i in range(b0, b1) if i in files]
            if not present:
                continue
            name = g['names'][0]
            gname = f'{b0:04d}_{slug(name)}'
            gdir = kdir if kind in ('effect', 'decor') else os.path.join(kdir, gname)
            os.makedirs(gdir, exist_ok=True)
            views = g['views']
            sheet = {}
            for i in present:
                off = i - b0
                frame, view = divmod(off, views)
                part = ''
                if kind == 'npc' and g['frames'] is not None and off >= views * g['frames']:
                    part = 'x'  # attack / special frames after the walk block
                    frame, view = off - views * g['frames'], 0
                fn = f'{gname}.png' if kind in ('effect', 'decor') else f'{part}f{frame:02d}_v{view}_{i:04d}.png'
                shutil.copyfile(files[i], os.path.join(gdir, fn))
                sheet[(part, frame, view)] = files[i]
                rows.append(dict(shape=i, kind=kind, group=gname, names='; '.join(g['names']),
                                 views=views, frame=f'{part}{frame}', view=view, file=os.path.relpath(os.path.join(gdir, fn), a.out)))
            if kind in ('effect', 'decor'):
                continue
            keys = sorted(sheet)
            lines = []
            for k in keys:
                line = (k[0], k[1])
                if line not in lines:
                    lines.append(line)
            imgs = {k: read_png_rgba(p) for k, p in sheet.items()}
            cw = max(w for w, _, _ in imgs.values()); ch = max(h for _, h, _ in imgs.values())
            ncol = max(views, max(k[2] for k in keys) + 1)
            W, H = cw * ncol, ch * len(lines)
            canvas = [bytearray(b'\x30\x30\x40' * W) for _ in range(H)]
            for k, (w, h, r) in imgs.items():
                ox = k[2] * cw; oy = lines.index((k[0], k[1])) * ch
                for y in range(h):
                    src = r[y]
                    for x in range(w):
                        if src[4 * x + 3]:
                            canvas[oy + y][3 * (ox + x):3 * (ox + x) + 3] = src[4 * x:4 * x + 3]
            write_png_rgb(os.path.join(kdir, f'{gname}.png'), W, H, canvas)
    with open(os.path.join(a.out, 'manifest.csv'), 'w', newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader(); w.writerows(rows)
    from collections import Counter
    c = Counter(r['kind'] for r in rows); g = Counter((r['kind'], r['group']) for r in rows)
    print('kind bases', kb)
    print('shapes per kind', dict(c))
    print('groups per kind', dict(Counter(k for k, _ in g)))

if __name__ == '__main__':
    main()
