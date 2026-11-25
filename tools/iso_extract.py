#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Minimal ISO 9660 extractor (primary volume descriptor only, stdlib only).

usage: iso_extract.py IMAGE OUTDIR [--list]
Keeps original timestamps. Strips ';1' version suffixes.
"""
import os, struct, sys, datetime

SECTOR = 2048


def read_dir(f, lba, size):
    f.seek(lba * SECTOR)
    data = f.read(size)
    pos = 0
    while pos < len(data):
        ln = data[pos]
        if ln == 0:  # pad to next sector
            pos = (pos // SECTOR + 1) * SECTOR
            continue
        rec = data[pos:pos + ln]
        ext = struct.unpack_from('<I', rec, 2)[0]
        dlen = struct.unpack_from('<I', rec, 10)[0]
        y, mo, d, h, mi, s = rec[18:24]
        flags = rec[25]
        nlen = rec[32]
        name = rec[33:33 + nlen]
        pos += ln
        if name in (b'\x00', b'\x01'):
            continue
        name = name.decode('ascii', 'replace').split(';')[0].rstrip('.')
        try:
            ts = datetime.datetime(1900 + y, mo, d, h, mi, s).timestamp()
        except ValueError:
            ts = None
        yield name, ext, dlen, bool(flags & 2), ts


def walk(f, lba, size, rel, out, listing):
    for name, ext, dlen, isdir, ts in read_dir(f, lba, size):
        path = os.path.join(rel, name)
        if isdir:
            if listing:
                print(f'{"<dir>":>12}  {path}/')
            else:
                os.makedirs(os.path.join(out, path), exist_ok=True)
            walk(f, ext, dlen, path, out, listing)
            continue
        if listing:
            print(f'{dlen:12d}  {path}')
            continue
        dst = os.path.join(out, path)
        f.seek(ext * SECTOR)
        left = dlen
        with open(dst, 'wb') as o:
            while left:
                chunk = f.read(min(left, 1 << 20))
                if not chunk:
                    raise IOError(f'short read in {path}')
                o.write(chunk)
                left -= len(chunk)
        if ts:
            os.utime(dst, (ts, ts))


def main():
    a = sys.argv[1:]
    listing = '--list' in a
    a = [x for x in a if x != '--list']
    if len(a) != 2:
        sys.exit(__doc__)
    img, out = a
    with open(img, 'rb') as f:
        f.seek(16 * SECTOR)
        pvd = f.read(SECTOR)
        if pvd[1:6] != b'CD001' or pvd[0] != 1:
            sys.exit('not an ISO 9660 primary volume descriptor')
        print('volume:', pvd[40:72].decode('ascii', 'replace').strip())
        root = pvd[156:156 + 34]
        lba = struct.unpack_from('<I', root, 2)[0]
        size = struct.unpack_from('<I', root, 10)[0]
        if not listing:
            os.makedirs(out, exist_ok=True)
        walk(f, lba, size, '', out, listing)


if __name__ == '__main__':
    main()
