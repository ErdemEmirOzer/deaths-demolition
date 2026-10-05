#!/usr/bin/env python3
"""WP-081 (C-00 05.10, decision ii) · wallpaper intake: a lossless master (PNG, 2560×1440) becomes the repo's JPEG.

The master stays where it was delivered (AP-028 → K/asset-production/AP-028/…); the repo carries the JPEG that Unity
compresses to BC7 anyway. One encoder for every wallpaper (Pillow JPEG, quality 90, 4:4:4 chroma, no metadata but the
sRGB profile the master carries), measured against its master before it is written:

  PSNR (RGB, 8-bit)        must reach --min-psnr (default 40 dB)
  SSIM (luma, Wang 2004)   reported (11-tap Gaussian, sigma 1.5, K1 0.01, K2 0.03)
  luminance ceiling        --ceiling L: the JPEG's brightest pixel (WCAG relative luminance, as UiContrast) stays ≤ L
                           when the master's does (the round-end wallpaper's 0.0245 clamp, UpgradeWallpaperTests).
                           JPEG ringing lifts single pixels over a ceiling the master keeps (q90: 85 of 3.7 M pixels,
                           0.0305); the encoder then dims only the 8×8 blocks that ring over it, in linear light, and
                           encodes again until none does ("ceilingFit": blocks, rounds, smallest scale - recorded).

  text-safe                --text-safe (loading key arts): LoadingKeyArtTests' own measure on the decoded JPEG - on its four
                           screens at push-in 1.0 / 1.05, the brightest pixel under the ring (cream ≥ 3:1, i.e. luminance
                           ≤ 0.25), under the label stack (TextSecondary ≥ 4.5:1, ≤ 0.061) and under the tip strip
                           (TextPrimary ≥ 7:1 on the 0.72 strip). The words sit inside x 0.35-0.65, y 0.30-0.72 (from the
                           top) - the band to paint dark, never a box. A pre-check for painters and Codex; the Unity test
                           stays the gate.

and recorded in UI/Art/wallpaper-intake.json (file, bytes, sha256, master sha256, encoder, PSNR, SSIM, max luminance),
which WallpaperIntakeTests checks against every file in Resources/UI/Wallpapers.

Usage: tools/wallpaper/wallpaper_intake.py --src MASTER.png --name NAME --master-ref TEXT [--quality 90]
                                           [--min-psnr 40] [--ceiling L] [--note TEXT] [--dry-run]
       tools/wallpaper/wallpaper_intake.py --adopt FILE --master-ref TEXT [--note TEXT]
         records a file already in Resources/UI/Wallpapers as it is (pre-intake art): sha256 and bytes, no encode.
Needs a python3 with Pillow and numpy (nothing is installed by this script).
Exit: 0 written (or measured, --dry-run) · 1 a check failed (nothing written) · 2 wrong master · 64 usage.
"""
import argparse
import datetime
import hashlib
import io
import json
import os
import sys

try:
    import numpy as np
    from PIL import Image, __version__ as PIL_VERSION
except ImportError as missing:  # pragma: no cover - environment check
    print("needs Pillow and numpy: %s" % missing, file=sys.stderr)
    sys.exit(64)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ART = os.path.join(ROOT, "prototype", "DeathsDemolition_PlayableConcept", "Assets", "_Prototype", "UI", "Art")
FOLDER = os.path.join(ART, "Resources", "UI", "Wallpapers")
RECORD = os.path.join(ART, "wallpaper-intake.json")
SCHEMA = "dd-wallpaper-intake-v1"
SIZE = (2560, 1440)

# WP-081: the loading screen's words over its key art - the geometry of LoadingKeyArtTests (UiCurtain: ring Ø 240 lifted
# 40, the label stack under it, the tip strip at the foot), on its four screens and at push-in 1.0 and 1.05.
SCREENS = (("1080", 1920, 1080, 1.0, 1.0), ("mac", 3024, 1964, 1.2, 1.25), ("16x10", 2560, 1600, 1.0, 1.0),
           ("21x9", 2560, 1080, 1.0, 1.0))
RING, RING_LIFT, SPACE_XL, SPACE_S, SPACE_M = 240.0, 40.0, 24.0, 8.0, 12.0
LABEL_LINE, H2_LINE, BODY_LINE, LABEL_HALF = 24.0, 32.0, 36.0, 180.0   # UiFactory.LineHeight at text 1.0
TIP_MARGIN, TIP_MAX, TIP_LINES, TIP_STRIP_ALPHA = 96.0, 720.0, 3, 0.72
TEXT_PRIMARY, TEXT_SECONDARY, SURFACE_BASE = "#F3EEE3", "#B9B2A6", "#1A1816"


def _hex_lin(h):
    return to_linear(np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], dtype=np.float64))


def _ratio(fg_l, bg_l):
    hi, lo = max(fg_l, bg_l), min(fg_l, bg_l)
    return (hi + 0.05) / (lo + 0.05)


def _brightest(lin, lum, rect, canvas, uv):
    """The brightest texel under a canvas rect (x, y, w, h from the bottom-left), at both push-ins (linear RGB)."""
    h, w = lum.shape
    best, best_rgb = -1.0, None
    for push in (1.0, 1.05):
        def to_uv(px, py):
            lx, ly = 0.5 + (px - 0.5) / push, 0.5 + (py - 0.5) / push
            return uv[0] + lx * uv[2], uv[1] + ly * uv[3]
        u0, v0 = to_uv(rect[0] / canvas[0], rect[1] / canvas[1])
        u1, v1 = to_uv((rect[0] + rect[2]) / canvas[0], (rect[1] + rect[3]) / canvas[1])
        x0 = min(max(int(np.floor(u0 * w)), 0), w - 1)
        x1 = min(max(int(np.ceil(u1 * w)), x0 + 1), w)
        y0 = min(max(int(np.floor(v0 * h)), 0), h - 1)
        y1 = min(max(int(np.ceil(v1 * h)), y0 + 1), h)
        block = lum[h - y1:h - y0, x0:x1]   # texture rows count from the bottom
        i = np.unravel_index(int(block.argmax()), block.shape)
        if block[i] > best:
            best, best_rgb = float(block[i]), lin[h - y1 + i[0], x0 + i[1]]
    return best, best_rgb


def text_safe(rgb, tip=True):
    """Each screen's ring / labels / tip contrast as LoadingKeyArtTests measures it, and whether all pass (3 / 4.5 / 7)."""
    lin = to_linear(rgb.astype(np.float64))
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    art = rgb.shape[1] / rgb.shape[0]
    tp, ts, base = _hex_lin(TEXT_PRIMARY), _hex_lin(TEXT_SECONDARY), _hex_lin(SURFACE_BASE)
    lum_of = lambda c: float(0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2])
    out, ok = {}, True
    for name, sw, sh, ui, text in SCREENS:
        ref = (1920.0 / ui, 1080.0 / ui)
        fit = min(sw / ref[0], sh / ref[1])
        canvas = (sw / fit, sh / fit)
        aspect = canvas[0] / canvas[1]
        uv = (0.0, (1 - art / aspect) / 2, 1.0, art / aspect) if aspect >= art else ((1 - aspect / art) / 2, 0.0, aspect / art, 1.0)
        cx, cy = canvas[0] * 0.5, canvas[1] * 0.5 + RING_LIFT
        ring = (cx - RING / 2, cy - RING / 2, RING, RING)
        top = cy - RING / 2 - SPACE_XL
        tall = (LABEL_LINE * 2 + SPACE_S + H2_LINE) * text
        half = LABEL_HALF * text
        labels = (cx - half, top - tall, half * 2, tall)
        tip_w = min(TIP_MAX, canvas[0] - TIP_MARGIN * 2)
        tip_rect = (TIP_MARGIN, TIP_MARGIN, tip_w, TIP_LINES * BODY_LINE * text + SPACE_M * 2)
        ring_l = _brightest(lin, lum, ring, canvas, uv)[0]
        label_l = _brightest(lin, lum, labels, canvas, uv)[0]
        r = {"ring": round(_ratio(lum_of(tp), ring_l), 2), "labels": round(_ratio(lum_of(ts), label_l), 2)}
        if tip:
            bg = _brightest(lin, lum, tip_rect, canvas, uv)[1]
            strip = base * TIP_STRIP_ALPHA + bg * (1 - TIP_STRIP_ALPHA)   # the project blends in linear light
            r["tip"] = round(_ratio(lum_of(tp), lum_of(strip)), 2)
        ok = ok and r["ring"] >= 3 and r["labels"] >= 4.5 and (not tip or r["tip"] >= 7)
        out[name] = r
    return out, ok


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def to_linear(srgb):
    c = srgb / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def max_luminance(rgb):
    lin = to_linear(rgb.astype(np.float64))
    return float((0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]).max())


def psnr(a, b):
    mse = np.mean((a.astype(np.float64) - b.astype(np.float64)) ** 2)
    return float("inf") if mse == 0 else float(10.0 * np.log10(255.0 * 255.0 / mse))


def _blur(x, kernel):
    pad = len(kernel) // 2
    x = np.pad(x, ((pad, pad), (0, 0)), mode="reflect")
    x = sum(kernel[i] * x[i:i + x.shape[0] - 2 * pad, :] for i in range(len(kernel)))
    x = np.pad(x, ((0, 0), (pad, pad)), mode="reflect")
    return sum(kernel[i] * x[:, i:i + x.shape[1] - 2 * pad] for i in range(len(kernel)))


def ssim(a, b):
    """Mean SSIM on BT.601 luma, 8-bit range (Wang et al. 2004, Gaussian window 11 / sigma 1.5)."""
    weights = np.array([0.299, 0.587, 0.114])
    x = a.astype(np.float64) @ weights
    y = b.astype(np.float64) @ weights
    g = np.exp(-((np.arange(11) - 5) ** 2) / (2 * 1.5 ** 2))
    g /= g.sum()
    c1, c2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    mx, my = _blur(x, g), _blur(y, g)
    sxx = _blur(x * x, g) - mx * mx
    syy = _blur(y * y, g) - my * my
    sxy = _blur(x * y, g) - mx * my
    s = ((2 * mx * my + c1) * (2 * sxy + c2)) / ((mx * mx + my * my + c1) * (sxx + syy + c2))
    return float(s.mean())


def encode(rgb, quality, icc):
    out = io.BytesIO()
    options = {"quality": quality, "subsampling": 0, "optimize": True, "progressive": False}
    if icc:
        options["icc_profile"] = icc
    Image.fromarray(rgb).save(out, "JPEG", **options)
    return out.getvalue()


def luminance(rgb):
    lin = to_linear(rgb.astype(np.float64))
    return 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]


def to_srgb8(lin):
    srgb = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.maximum(lin, 0.0), 1.0 / 2.4) - 0.055)
    return np.clip(np.round(srgb * 255.0), 0, 255).astype(np.uint8)


def fit_ceiling(rgb, quality, icc, ceiling, rounds=24):
    """Encode; while a decoded pixel rings over the ceiling, dim that pixel's 8×8 block (linear light) and encode again."""
    work = rgb.copy()
    lin = to_linear(rgb.astype(np.float64))
    scale = {}
    for done in range(rounds + 1):
        data = encode(work, quality, icc)
        decoded = np.asarray(Image.open(io.BytesIO(data)).convert("RGB"))
        lum = luminance(decoded)
        over = np.argwhere(lum > ceiling)
        if len(over) == 0:
            fit = {"blocks": len(scale), "rounds": done, "minScale": round(min(scale.values()), 4) if scale else 1.0}
            return data, decoded, fit
        for by, bx in {(int(y) // 8, int(x) // 8) for y, x in over}:
            ys, xs = slice(by * 8, by * 8 + 8), slice(bx * 8, bx * 8 + 8)
            factor = scale.get((by, bx), 1.0) * min(0.97, 0.98 * ceiling / float(lum[ys, xs].max()))
            scale[(by, bx)] = factor
            work[ys, xs] = to_srgb8(lin[ys, xs] * factor)
    return None, None, {"blocks": len(scale), "rounds": rounds, "minScale": round(min(scale.values()), 4)}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--src")
    parser.add_argument("--name", help="the Resources name, without extension (loading_L01)")
    parser.add_argument("--adopt", help="a file name in Resources/UI/Wallpapers to record as it is")
    parser.add_argument("--master-ref", required=True, help="where the master lives (K/asset-production/AP-028/…, or git:<sha>:<path>)")
    parser.add_argument("--quality", type=int, default=90)
    parser.add_argument("--min-psnr", type=float, default=40.0)
    parser.add_argument("--ceiling", type=float, default=None)
    parser.add_argument("--note", default="")
    parser.add_argument("--text-safe", action="store_true", help="a loading key art: the words' zones under their ceilings")
    parser.add_argument("--dry-run", action="store_true")
    try:
        args = parser.parse_args()
    except SystemExit:
        sys.exit(64)
    if args.adopt:
        path = os.path.join(FOLDER, args.adopt)
        if not os.path.isfile(path):
            print("no %s in %s" % (args.adopt, FOLDER), file=sys.stderr)
            sys.exit(2)
        data = open(path, "rb").read()
        row = {"file": args.adopt, "bytes": len(data), "sha256": sha256(data),
               "master": {"path": args.master_ref, "sha256": "", "bytes": 0}, "encoder": "adopted (pre-intake)",
               "psnr": 0.0, "ssim": 0.0, "maxLuminance": None, "ceilingFit": None,
               "date": datetime.date.today().isoformat(), "note": args.note}
        if not args.dry_run:
            write_record(row)
        print(json.dumps(row, ensure_ascii=False, indent=2))
        return
    if not args.src or not args.name:
        print("--src and --name (or --adopt)", file=sys.stderr)
        sys.exit(64)

    raw = open(args.src, "rb").read()
    master = Image.open(io.BytesIO(raw))
    master.load()
    if master.size != SIZE:
        print("master is %dx%d, wallpapers are %dx%d" % (master.size + SIZE), file=sys.stderr)
        sys.exit(2)
    if master.mode not in ("RGB", "RGBA", "P"):
        print("master mode %s is not RGB" % master.mode, file=sys.stderr)
        sys.exit(2)
    if master.mode == "RGBA" and master.getextrema()[3][0] < 255:
        print("master has transparent pixels; a wallpaper is opaque", file=sys.stderr)
        sys.exit(2)

    a = np.asarray(master.convert("RGB"))
    icc = master.info.get("icc_profile")
    fit = None
    if args.ceiling is not None and max_luminance(a) <= args.ceiling:
        data, b, fit = fit_ceiling(a, args.quality, icc, args.ceiling)
        if data is None:
            print("the ceiling could not be fitted in %d rounds (%s)" % (fit["rounds"], fit), file=sys.stderr)
            sys.exit(1)
    else:
        data = encode(a, args.quality, icc)
        b = np.asarray(Image.open(io.BytesIO(data)).convert("RGB"))
    row = {
        "file": args.name + ".jpg",
        "bytes": len(data),
        "sha256": sha256(data),
        "master": {"path": args.master_ref, "sha256": sha256(raw), "bytes": len(raw)},
        "encoder": "Pillow %s JPEG q%d 4:4:4 optimize" % (PIL_VERSION, args.quality),
        "psnr": round(psnr(a, b), 2),
        "ssim": round(ssim(a, b), 5),
        "maxLuminance": {"master": round(max_luminance(a), 5), "jpeg": round(max_luminance(b), 5)},
        "ceilingFit": fit,
        "textSafe": text_safe(b)[0] if args.text_safe else None,
        "date": datetime.date.today().isoformat(),
        "note": args.note,
    }
    print(json.dumps(row, ensure_ascii=False, indent=2))

    failures = []
    if row["psnr"] < args.min_psnr:
        failures.append("PSNR %.2f < %.2f" % (row["psnr"], args.min_psnr))
    if args.ceiling is not None and row["maxLuminance"]["master"] <= args.ceiling < row["maxLuminance"]["jpeg"]:
        failures.append("the JPEG's brightest pixel %.5f passes the ceiling %.5f the master keeps" % (row["maxLuminance"]["jpeg"], args.ceiling))
    if args.text_safe and not text_safe(b)[1]:
        failures.append("not text-safe under the loading words: " + json.dumps(row["textSafe"]))
    if failures:
        print("NOT WRITTEN: " + "; ".join(failures), file=sys.stderr)
        sys.exit(1)
    if args.dry_run:
        return

    with open(os.path.join(FOLDER, row["file"]), "wb") as handle:
        handle.write(data)
    write_record(row)
    print("written %s (%d B) and its record" % (row["file"], row["bytes"]))


def write_record(row):
    record = {"schema": SCHEMA, "files": []}
    if os.path.exists(RECORD):
        record = json.load(open(RECORD, encoding="utf-8"))
    record["files"] = [r for r in record["files"] if r["file"] != row["file"]] + [row]
    record["files"].sort(key=lambda r: r["file"])
    with open(RECORD, "w", encoding="utf-8") as handle:
        json.dump(record, handle, ensure_ascii=False, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main()
