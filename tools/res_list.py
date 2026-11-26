#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Print the directory of a RES file as a table (see docs/formats/resources.md).

Columns: index, offset, size, kind, EH type, header length, compression, unpacked size.
The format matches the Unity ResDirectoryLogger rows, so the two outputs can be diffed.
Prints only offsets, sizes and header fields, never entry contents.

  kind:  EH = starts with "EH"; raw = data without "EH"; empty = size 0;
         invalid = offset outside the file or before the previous entry
  '-'    = field not present for this entry

usage: res_list.py RESFILE [--no-header]
"""
import argparse, struct, sys

HEADER = 'index     offset     size kind    type hlen comp unpacked'


def entries(d):
    n = len(d)
    if n < 4:
        raise ValueError(f'{n} bytes, too short for an entry count')
    count, = struct.unpack_from('<I', d, 0)
    table_end = 4 + 4 * count
    if table_end > n:
        raise ValueError(f'count {count} needs a {table_end}-byte table, file has {n} bytes')
    offs = struct.unpack_from(f'<{count}I', d, 4)
    for i, off in enumerate(offs):
        end = offs[i + 1] if i + 1 < count else n
        e = dict(index=i, offset=off, size=0, kind='invalid', type=-1, hlen=-1, comp=-1, unpacked=-1)
        if off < table_end or off > n or end < off or end > n:
            yield e; continue
        e['size'] = size = end - off
        if size == 0:
            e['kind'] = 'empty'; yield e; continue
        if size < 5 or d[off:off + 2] != b'EH':
            e['kind'] = 'raw'; yield e; continue
        e['kind'] = 'EH'
        t = e['type'] = d[off + 2]
        hl = e['hlen'] = struct.unpack_from('<h', d, off + 3)[0]
        if hl < 0 or 5 + hl > size:
            yield e; continue
        if t == 5 and hl >= 5:                      # M, P textures: u8 compression, u32 unpacked
            e['comp'] = d[off + 5]; e['unpacked'] = struct.unpack_from('<I', d, off + 6)[0]
        elif t == 6 and hl >= 3:                    # M, P column shapes: u8 compression, u16 unpacked
            e['comp'] = d[off + 5]; e['unpacked'] = struct.unpack_from('<H', d, off + 6)[0]
        elif t in (6, 7) and hl == 0 and size >= 7:  # S: u16 unpacked, then LZSS
            e['comp'] = 3; e['unpacked'] = struct.unpack_from('<H', d, off + 5)[0]
        yield e


def row(e):
    f = lambda v: '-' if v < 0 else str(v)
    return (f"{e['index']:>5} {e['offset']:>10} {e['size']:>8} {e['kind']:<7} "
            f"{f(e['type']):>4} {f(e['hlen']):>4} {f(e['comp']):>4} {f(e['unpacked']):>8}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('res')
    ap.add_argument('--no-header', action='store_true')
    a = ap.parse_args()
    with open(a.res, 'rb') as fh:
        d = fh.read()
    try:
        rows = [row(e) for e in entries(d)]
    except ValueError as ex:
        sys.exit(f'{a.res}: {ex}')
    if not a.no_header:
        print(HEADER)
    print('\n'.join(rows))


if __name__ == '__main__':
    main()
