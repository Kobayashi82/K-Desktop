#!/usr/bin/env python3
"""Contact sheet of PNG stills (for reviewing): python tools/sheet.py out.png a.png b.png … [--cols 3]"""
import sys
from PIL import Image, ImageDraw

args = sys.argv[1:]
cols = 3
if "--cols" in args:
    i = args.index("--cols"); cols = int(args[i + 1]); del args[i:i + 2]
out, files = args[0], args[1:]
w, h = 640, 360
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h), (40, 40, 40))
for k, f in enumerate(files):
    im = Image.open(f).convert("RGB").resize((w, h), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, 150, 22], fill=(0, 0, 0))
    d.text((5, 5), f.split("/")[-1], fill=(255, 255, 0))
    sheet.paste(im, ((k % cols) * w, (k // cols) * h))
sheet.save(out)
