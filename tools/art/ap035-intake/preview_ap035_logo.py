#!/usr/bin/env python3
"""AP-035 intake review sheet (not shipped): the baked logo's end state composited as UiLogoAnimator lays it out -
the loading lockup over the rotation key arts (halo + seal backing at their tints), the ring's slot outlined, and the
menu's stacked composition over its ink field. Straight-alpha over, sRGB space (a review approximation of the GPU's
linear blend). Writes out/previews/loading_*.png and menu_stacked.png."""
import os

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
PREV = os.path.join(OUT, "previews")
# the checkout this script lives in (was the wp-098 worktree, spelled out — test_dd_kadro: no KADRO path in a tool;
# the five wallpapers are the same blobs on main)
W = os.path.normpath(os.path.join(HERE, "..", "..", "..", "prototype", "DeathsDemolition_PlayableConcept"))
ARTS = W + "/Assets/_Prototype/UI/Art/Resources/UI/Wallpapers/"

SUMI900 = (0x12, 0x10, 0x0F)
HALO_ALPHA, BACKING_ALPHA = 0.85, 0.92          # UiLogoAnimator.HaloAlpha / SealBackingAlpha
K = 900.0 / 1286.0                              # lockup units -> spec px (the wordmark ink width)
LOCKUP = {"wordmark": (209.95, 0.0), "seal": (-495.5, 0.0), "seal_scale": 340.1 / 462.85, "ink_h": 340.1}
STACKED = {"wordmark": (0.0, -278.0), "seal": (0.0, 177.0), "seal_scale": 1.0, "ink_h": 836.0}
SEAL_SPRITE = 481.494


def load(name):
    return Image.open(os.path.join(OUT, name)).convert("RGBA")


def tint(mask, rgb, alpha):
    a = (np.asarray(mask)[..., 3].astype(np.float64) * alpha).round().astype(np.uint8)
    return Image.fromarray(np.dstack([np.full(a.shape, c, np.uint8) for c in rgb] + [a]), "RGBA")


def place(canvas, img, cx, cy, w, h):
    img = img.resize((max(1, int(round(w))), max(1, int(round(h)))), Image.BILINEAR)
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(img, (int(round(cx - w / 2)), int(round(cy - h / 2))))
    return Image.alpha_composite(canvas, layer)


def compose(canvas, layout, scale, centre, backing):
    wm, seal = load("ap035_logo_wordmark_2048x768.png"), load("ap035_logo_seal_768.png")
    halo, back = load("ap035_logo_wordmark_halo_1024x384.png"), load("ap035_logo_seal_backing_256.png")
    cx, cy = centre
    sx, sy = cx + layout["seal"][0] * scale, cy - layout["seal"][1] * scale
    wx, wy = cx + layout["wordmark"][0] * scale, cy - layout["wordmark"][1] * scale
    ss = SEAL_SPRITE * layout["seal_scale"] * scale
    if backing:
        canvas = place(canvas, tint(back, SUMI900, BACKING_ALPHA), sx, sy, ss, ss)
        canvas = place(canvas, tint(halo, SUMI900, HALO_ALPHA), wx, wy, 960 * scale, 360 * scale)
    canvas = place(canvas, seal, sx, sy, ss, ss)
    return place(canvas, wm, wx, wy, 960 * scale, 360 * scale)


def main():
    os.makedirs(PREV, exist_ok=True)
    ink_h = 280.0
    scale = ink_h / LOCKUP["ink_h"]
    centre = (960.0, 540.0 - (160.0 + 540.0) / 2.0)   # half way between the ring's top (+160) and the canvas top
    sheet = []
    for name in ("loading_L01", "loading_L02", "loading_L04", "loading_L05", "loading_L08"):
        art = Image.open(ARTS + name + ".jpg").convert("RGBA").resize((1920, 1080), Image.LANCZOS)
        frame = compose(art, LOCKUP, scale, centre, True)
        d = ImageDraw.Draw(frame)
        d.ellipse((960 - 120, 540 - 40 - 120, 960 + 120, 540 - 40 + 120), outline=(243, 238, 227, 255), width=3)
        frame.convert("RGB").save(os.path.join(PREV, name + "_logo.png"))
        sheet.append(frame.convert("RGB").resize((960, 540)))
    grid = Image.new("RGB", (1920, 540 * 3), (40, 40, 40))
    for i, t in enumerate(sheet):
        grid.paste(t, ((i % 2) * 960, (i // 2) * 540))
    field = Image.new("RGBA", (1920, 1080), SUMI900 + (255,))
    grid.paste(compose(field, STACKED, 1.0, (960.0, 540.0), False).convert("RGB").resize((960, 540)), (960, 1080))
    grid.save(os.path.join(PREV, "loading_and_menu_sheet.png"))


if __name__ == "__main__":
    main()
