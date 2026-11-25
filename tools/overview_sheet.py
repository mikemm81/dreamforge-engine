#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Labelled overview sheets of grouped shapes, for naming groups by eye.

usage: overview_sheet.py GROUPED_KIND_DIR OUTPREFIX [--cols 6 --rows 5 --cell 112]
For each group folder (<base>_<name>/) takes its first image (f00_v0_* or lowest index), crops it to
its drawn pixels, scales it to fit a cell, and labels the cell with the group's base number.
Writes OUTPREFIX_00.png, OUTPREFIX_01.png, ...
"""
import os, sys, argparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from res3_groups import read_png_rgba, write_png_rgb

DIGITS = {  # 3x5 pixel font
    '0': '111101101101111', '1': '010110010010111', '2': '111001111100111', '3': '111001111001111',
    '4': '101101111001001', '5': '111100111001111', '6': '111100111101111', '7': '111001001001001',
    '8': '111101111101111', '9': '111101111001111'}

def draw_text(canvas, x, y, text, scale=2, col=b'\xff\xff\x40'):
    for ch in text:
        bits = DIGITS[ch]
        for r in range(5):
            for c in range(3):
                if bits[r * 3 + c] == '1':
                    for dy in range(scale):
                        for dx in range(scale):
                            yy, xx = y + r * scale + dy, x + c * scale + dx
                            canvas[yy][3 * xx:3 * xx + 3] = col
        x += 4 * scale

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('kind_dir'); ap.add_argument('out')
    ap.add_argument('--cols', type=int, default=6); ap.add_argument('--rows', type=int, default=5)
    ap.add_argument('--cell', type=int, default=112)
    a = ap.parse_args()
    groups = sorted(g for g in os.listdir(a.kind_dir) if os.path.isdir(os.path.join(a.kind_dir, g)))
    per = a.cols * a.rows; C = a.cell; LH = 14
    for page in range((len(groups) + per - 1) // per):
        W, H = a.cols * C, a.rows * (C + LH)
        canvas = [bytearray(b'\x50\x50\x58' * W) for _ in range(H)]
        for k, g in enumerate(groups[page * per:(page + 1) * per]):
            d = os.path.join(a.kind_dir, g)
            fs = sorted(os.listdir(d))
            first = next((f for f in fs if f.startswith('f00_v0_')), fs[0])
            w, h, rows = read_png_rgba(os.path.join(d, first))
            pts = [(x, y) for y in range(h) for x in range(w) if rows[y][4 * x + 3]]
            ox, oy = (k % a.cols) * C, (k // a.cols) * (C + LH)
            for y in range(oy, oy + C + LH):  # cell border
                canvas[y][3 * ox:3 * ox + 3] = b'\x20\x20\x20'
            draw_text(canvas, ox + 3, oy + 2, g.split('_')[0])
            if not pts:
                continue
            x0 = min(p[0] for p in pts); x1 = max(p[0] for p in pts) + 1
            y0 = min(p[1] for p in pts); y1 = max(p[1] for p in pts) + 1
            cw, ch = x1 - x0, y1 - y0
            s = min((C - 4) / cw, (C - 4) / ch, 3.0)
            tw, th = max(1, int(cw * s)), max(1, int(ch * s))
            bx, by = ox + (C - tw) // 2, oy + LH + (C - th) // 2
            for ty in range(th):
                sy = y0 + int(ty / s); src = rows[sy]
                for tx in range(tw):
                    sx = x0 + int(tx / s)
                    if src[4 * sx + 3]:
                        canvas[by + ty][3 * (bx + tx):3 * (bx + tx) + 3] = src[4 * sx:4 * sx + 3]
        write_png_rgb(f'{a.out}_{page:02d}.png', W, H, canvas)
        print(f'{a.out}_{page:02d}.png')

if __name__ == '__main__':
    main()
