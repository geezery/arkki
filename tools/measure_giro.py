#!/usr/bin/env python3
"""Renders the last page of an invoice PDF at 600 dpi and measures the credit transfer form:
line positions and thicknesses, barcode size and quiet zones. Compare against the values of
Finance Finland's Tilisiirto-opas 2.16 and Bank Bar Code Guide 5.3.
Usage: measure_giro.py invoice.pdf <last page number>"""
import subprocess, sys, tempfile, os
from PIL import Image
import numpy as np
Image.MAX_IMAGE_PIXELS = None
pdf, page = sys.argv[1], sys.argv[2]
tmp = tempfile.mkdtemp()
subprocess.run(["pdftoppm", "-r", "600", "-f", page, "-l", page, "-png", pdf, os.path.join(tmp, "p")], check=True)
im = np.array(Image.open(os.path.join(tmp, os.listdir(tmp)[0])).convert("L"))
H, W = im.shape
dpmm = 600 / 25.4
top = H - 101.6 * dpmm
dark = im < 128
T = lambda n: n * 25.4 / 12

def runs(v):
    out, s = [], None
    for i, x in enumerate(v):
        if x and s is None: s = i
        if not x and s is not None: out.append((s, i - 1)); s = None
    if s is not None: out.append((s, len(v) - 1))
    return out

print(f"page {W / dpmm:.2f} x {H / dpmm:.2f} mm")
print("horizontal lines: y from form top, x range, thickness (mm)")
y = int(top) - 10
while y < H:
    rs = [(a, b) for a, b in runs(dark[y]) if b - a > 40 * dpmm]
    if rs:
        y2 = y
        while y2 + 1 < H and any(b - a > 40 * dpmm for a, b in runs(dark[y2 + 1])): y2 += 1
        a, b = rs[0]
        print(f"  y={((y + y2 + 1) / 2 - top) / dpmm:7.3f}  x={a / dpmm:7.2f}..{(b + 1) / dpmm:7.2f}  t={(y2 - y + 1) / dpmm:.3f}")
        y = y2 + 1
    y += 1
print("  expected y:", [round(v, 3) for v in (0, T(8), T(15), T(27), T(29), T(31), T(35))])

print("vertical lines: x from left edge, y range from form top, thickness (mm)")
sub = dark[int(top):int(top + T(35) * dpmm) + 8]
x = 0
while x < W:
    rs = [(a, b) for a, b in runs(sub[:, x]) if b - a > 7 * dpmm]
    if rs:
        x2 = x
        while x2 + 1 < W and any(b - a > 7 * dpmm for a, b in runs(sub[:, x2 + 1])): x2 += 1
        for a, b in rs:
            print(f"  x={((x + x2 + 1) / 2) / dpmm:7.3f}  y={a / dpmm:6.2f}..{(b + 1) / dpmm:6.2f}  t={(x2 - x + 1) / dpmm:.3f}")
        x = x2 + 1
    x += 1
print("  expected x: 20.32, 111.76, 127.00, 160.02")

line = int(top + T(35) * dpmm)
row = dark[line + int(3 * dpmm)]
xs = np.where(row[:int(140 * dpmm)])[0]
col = dark[line + 10:, xs.min() + 3]
ys = np.where(col)[0]
right = np.where(dark[line + int(3 * dpmm):line + int(13 * dpmm), xs.max() + 5:].any(0))[0]
print(f"barcode: x={xs.min() / dpmm:.2f}..{(xs.max() + 1) / dpmm:.2f} mm, length {(xs.max() + 1 - xs.min()) / dpmm:.2f} mm (allowed 70-105), "
      f"height {(ys.max() - ys.min() + 1) / dpmm:.2f} mm (allowed 10.0-12.7)")
print(f"quiet zone left {xs.min() / dpmm:.2f} mm, right {(right.min() + 5) / dpmm:.2f} mm (20 required)")
