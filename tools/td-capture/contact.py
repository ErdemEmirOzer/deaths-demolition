#!/usr/bin/env python3
"""WP-099 (S-01, C-00 07.10) · td-capture: the stills' contact sheet — <stills>/contact.png, the PNGs in name order on a
4-column grid (each scaled to 480 px wide), the pose key under each. usage: contact.py <stills dir>   exit 0 · 2 no PNG"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

COLS, CELL_W, LABEL_H = 4, 480, 28


def main(argv):
    d = Path(argv[1])
    shots = sorted(p for p in d.glob("*.png") if p.name != "contact.png")
    if not shots:
        print("no PNG in %s" % d, file=sys.stderr)
        return 2
    first = Image.open(shots[0])
    cell_h = round(first.height * CELL_W / first.width)
    rows = (len(shots) + COLS - 1) // COLS
    sheet = Image.new("RGB", (COLS * CELL_W, rows * (cell_h + LABEL_H)), (16, 16, 18))
    draw = ImageDraw.Draw(sheet)
    for i, p in enumerate(shots):
        x, y = (i % COLS) * CELL_W, (i // COLS) * (cell_h + LABEL_H)
        sheet.paste(Image.open(p).convert("RGB").resize((CELL_W, cell_h), Image.LANCZOS), (x, y))
        draw.text((x + 8, y + cell_h + 6), p.stem, fill=(230, 230, 230))
    sheet.save(d / "contact.png")
    print("contact.png: %d stills" % len(shots))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
