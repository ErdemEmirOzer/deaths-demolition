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
    parser.add_argument("--name", help="the Resources name, without extension (loading_keyart_02)")
    parser.add_argument("--adopt", help="a file name in Resources/UI/Wallpapers to record as it is")
    parser.add_argument("--master-ref", required=True, help="where the master lives (K/asset-production/AP-028/…, or git:<sha>:<path>)")
    parser.add_argument("--quality", type=int, default=90)
    parser.add_argument("--min-psnr", type=float, default=40.0)
    parser.add_argument("--ceiling", type=float, default=None)
    parser.add_argument("--note", default="")
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
        "date": datetime.date.today().isoformat(),
        "note": args.note,
    }
    print(json.dumps(row, ensure_ascii=False, indent=2))

    failures = []
    if row["psnr"] < args.min_psnr:
        failures.append("PSNR %.2f < %.2f" % (row["psnr"], args.min_psnr))
    if args.ceiling is not None and row["maxLuminance"]["master"] <= args.ceiling < row["maxLuminance"]["jpeg"]:
        failures.append("the JPEG's brightest pixel %.5f passes the ceiling %.5f the master keeps" % (row["maxLuminance"]["jpeg"], args.ceiling))
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
