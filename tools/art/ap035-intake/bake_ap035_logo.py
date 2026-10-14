#!/usr/bin/env python3
"""AP-035 intake bake (WP-098A, C-00 08.10; AP-035 R3 S-11 O2 08.10: 2048 Logo class, floor time code, 4-multiple fx,
--install) - the loading / menu logo rebuilt natively for Unity.

Inputs (read-only, AP-035 exports + one working sheet the motion spec names):
  v002 StrokeMasks: the 36 stroke masks (owner = which mask is non-zero), stroke_owner_ids.png (cross-check),
                    stroke_progress_u16.png (path progress), stroke-timeline.json (per-stroke frames + cubic-bezier)
  v003 LoadingLogo/motion-spec.json (wordmark window [3, 47], front softness), sync-points.json
  v001 wordmark_stacked_full_4096.png (the stroke master: weights for the time down-sample)
  v003 wordmark_stacked_full_8192.png (the colour art; 3 px right of the v001 master at 4096 - measured, shifted here)
  v001 emblem/dark_safe/emblem_dark_safe_4096.png (the seal: bone towers + vermilion broken ring)
  v001 LogoLayers seal_glow_fx.png, seal_ink_leak_fx.png, seal_body_black.png, seal_red_broken_ring.png (placement)
  03-*/v003/loading_ring_ash_atlas.png (the package's numbered working folder 03-*; exactly one must hold it; the closed ink ring, box [0, 0, 720, 752])

Outputs (out/): the eight runtime PNGs + bake-report.json; --previews adds previews/ (review sheets, not shipped, not in git).
--install copies the runtime PNGs into the Unity project (Logo class: Resources/UI/Logo, wordmark + time map + halo;
UI 1024 class: Resources/UI/LogoMark, seal + fx) and writes UI/Art/logo-intake.json (sha256 / bytes / placement / motion
tables the runtime constants are tested against). Same inputs -> same bytes (deterministic).

Time map contract (A8, linear, 8-bit): 0 = first ink ... 254 = last, 255 = never. A texel's value is
floor((T - 3) / 44 * 254) with T the frame (1-based PNG number) at which the owning stroke's eased path progress reaches
the texel's own path progress: T = start + (end - start) * x(s*), y(s*) = p for the stroke's cubic-bezier (x1, y1, x2, y2)
- the x-inversion the timeline asks for, solved by bisection. Floor, not round (R3): a texel's code never lies later than
its time, so the shader's one-code front (DD_UiFxLogo.hlsl: vis = saturate(p * 255 - code)) is fully inked inside the
texel's true interval - round() held texels just before a frame boundary back by one frame (the IoU 0.88 at frame 4).
Texels are the alpha-weighted mean of the FACTOR x FACTOR source pixels (4096 -> 2048); texels within DILATE texels of ink
take their inked neighbours' mean so a bilinear tap inside the colour art never reads 255; everything else is 255.
"""
import glob
import hashlib
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.spatial import ConvexHull

Image.MAX_IMAGE_PIXELS = None

K = "/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition"
AP = K + "/asset-production/AP-035"
EX = AP + "/exports"
STROKES = EX + "/v002/Logo/StrokeMasks"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
PREV = os.path.join(OUT, "previews")

# ---------------------------------------------------------------- parameters (INTAKE.md records them)
OUT_W, OUT_H = 2048, 768          # Logo texture class (C-00 08.10, K8): max edge 2048, class budget 8 MB (UiArtThemeTests)
SRC_W, SRC_H = 4096, 1536         # the stroke canvas
FACTOR = SRC_W // OUT_W           # 2
HALO_W, HALO_H = 1024, 384        # the halo is a soft band (>= 11 texels at 1024): half resolution keeps it (checks.halo_1024_vs_2048)
V003_SHIFT_X = 3                  # v003 8K art sits 3 px (at 4096) right of the v001 master: IoU 0.9654 -> 0.9998
WINDOW = (3.0, 47.0)              # motion-spec wordmark.window (1-based PNG frames)
DILATE = 32                       # texels (at 2048) of time padding around the ink: the halo inks in with its letters, bilinear safe
SEAL_TEX = 768                    # seal sprite edge
SEAL_CROP_PAD = 1.04              # square crop = max(ink w, h) * pad, centred on the ink box
FX_TEX = 384                      # glow / blot sprite long edge
RING_TEX = 512                    # impact ring sprite long edge
RING_BOX = (0, 0, 720, 752)       # motion-spec closed_ink_ring.source_box
SPEC_SEAL_LAYER = 570.0           # the seal layer in the 1920x1080 spec frame (= the 4096 emblem canvas)

FILES = {
    "timeline": STROKES + "/stroke-timeline.json",
    "owner": STROKES + "/stroke_owner_ids.png",
    "progress": STROKES + "/stroke_progress_u16.png",
    "motion_spec": EX + "/v003/LoadingLogo/motion-spec.json",
    "sync_points": EX + "/v003/LoadingLogo/sync-points.json",
    "master_4096": EX + "/v001/Logo/wordmark_stacked/full/wordmark_stacked_full_4096.png",
    "wordmark_8192": EX + "/v004/Logo/wordmark_stacked/full/wordmark_stacked_full_8192.png",   # O1 A5: v003 + the 8K seam fix (alpha identical)
    "owner_960": STROKES + "/stroke_owner_ids_960x360.png",
    "progress_repaired_960": AP + "/04-tools/v004/a1_stroke_progress_repaired_u16_960x360.png",   # O1 A1: plateaus -> ramps
    "emblem_4096": EX + "/v001/Logo/emblem/dark_safe/emblem_dark_safe_4096.png",
    "seal_glow": EX + "/v001/Logo/LogoLayers/seal_glow_fx.png",
    "seal_ink_leak": EX + "/v001/Logo/LogoLayers/seal_ink_leak_fx.png",
    "seal_body": EX + "/v001/Logo/LogoLayers/seal_body_black.png",
    "seal_ring": EX + "/v001/Logo/LogoLayers/seal_red_broken_ring.png",
    "ash_atlas": AP + "/03-*/v003/loading_ring_ash_atlas.png",   # a pattern: resolved to the one match below
}


def _resolve_pattern(pattern):
    """The one existing file a glob pattern names; any other count stops the bake with the rule and where to look."""
    found = sorted(glob.glob(pattern))
    if len(found) != 1:
        sys.exit("bake_ap035_logo: input pattern %s must match exactly one file, found %d %s - check the AP-035 working folder 03-*/v003"
                 % (pattern, len(found), found))
    return found[0]


REPORT_PATHS = {"ash_atlas": FILES["ash_atlas"]}   # the report names the pattern, so a re-run writes the same bytes anywhere
FILES["ash_atlas"] = _resolve_pattern(FILES["ash_atlas"])

NAMES = {
    "wordmark": "ap035_logo_wordmark_2048x768.png",
    "timemap": "ap035_logo_wordmark_timemap_2048x768.png",
    "seal": "ap035_logo_seal_768.png",
    "glow": "ap035_logo_seal_glow.png",
    "blot": "ap035_logo_seal_blot.png",
    "ring": "ap035_logo_impact_ring.png",
    "halo": "ap035_logo_wordmark_halo_1024x384.png",
    "backing": "ap035_logo_seal_backing_256.png",
}

# Unity folders (--install): the Logo class (2048) and the UI 1024 class.
LOGO_KEYS = ("wordmark", "timemap", "halo")
MARK_KEYS = ("seal", "glow", "blot", "ring", "backing")
PROJECT = "prototype/DeathsDemolition_PlayableConcept"
LOGO_DIR = PROJECT + "/Assets/_Prototype/UI/Art/Resources/UI/Logo"
MARK_DIR = PROJECT + "/Assets/_Prototype/UI/Art/Resources/UI/LogoMark"
INTAKE_JSON = PROJECT + "/Assets/_Prototype/UI/Art/logo-intake.json"

# O1 A1 (S1-05): the v002 progress map holds flat plateaus (caps at 0 / 1, ~0.1 interior steps); every texel of a plateau
# shares one time, so it inks all at once. The repaired 960x360 progress (plateaus -> Laplace ramps inside the stroke) is
# turned into times at 960 and sampled per owner at 4096 (bilinear inside the same stroke; outside it the nearest texel of
# that stroke) - stroke borders stay at 4096. False = the v002 4096 progress as before (the report keeps both counts).
REPAIRED_PROGRESS = True
PLATEAU_MIN_PX = 145              # 8 px at 960 (O1 A1's cut) in 4096 pixels: a same-time region this big inks at one instant

HALO_FULL, HALO_ZERO = 6.0, 28.0   # wordmark halo (2048 texels from the ink edge): alpha 1 within 6, 0 from 28 (= 3 / 14 at 1024)
BACK_FULL, BACK_ZERO = 2.0, 12.0   # seal backing (768 texels from the filled silhouette)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def bezier(a, b, s):
    u = 1.0 - s
    return 3.0 * u * u * s * a + 3.0 * u * s * s * b + s * s * s


def time_fraction(p, cp):
    """x(s*) where y(s*) = p for cubic-bezier (x1, y1, x2, y2) - bisection (y monotone for y1, y2 in [0, 1])."""
    x1, y1, x2, y2 = cp
    lo = np.zeros_like(p)
    hi = np.ones_like(p)
    for _ in range(40):
        mid = 0.5 * (lo + hi)
        below = bezier(y1, y2, mid) < p
        lo = np.where(below, mid, lo)
        hi = np.where(below, hi, mid)
    return bezier(x1, x2, 0.5 * (lo + hi))


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def repaired_times(timeline, owner):
    """Per-pixel reveal frame (4096) from O1's repaired 960x360 progress: times at 960 per stroke, then for each 4096 pixel
    a bilinear sample of its own stroke's time field (filled outside the stroke with the stroke's nearest texel)."""
    own9 = np.asarray(Image.open(FILES["owner_960"])).astype(np.int32)
    prog9 = np.asarray(Image.open(FILES["progress_repaired_960"])).astype(np.float64) / 65535.0
    h9, w9 = own9.shape
    T = np.full(owner.shape, np.nan)
    for s in timeline["strokes"]:
        m4 = owner == s["id"]
        m9 = own9 == s["id"]
        if not m4.any():
            continue
        assert m9.any(), "stroke %d missing at 960" % s["id"]
        t9 = s["start_frame"] + (s["end_frame"] - s["start_frame"]) * time_fraction(prog9[m9], s["curve"]["control_points"])
        field = np.zeros(own9.shape)
        field[m9] = t9
        _, (iy, ix) = ndimage.distance_transform_edt(~m9, return_indices=True)
        field = field[iy, ix]
        py, px = np.nonzero(m4)
        fx = np.clip((px + 0.5) * w9 / owner.shape[1] - 0.5, 0, w9 - 1)
        fy = np.clip((py + 0.5) * h9 / owner.shape[0] - 0.5, 0, h9 - 1)
        x0 = np.floor(fx).astype(int); y0 = np.floor(fy).astype(int)
        x1 = np.minimum(x0 + 1, w9 - 1); y1 = np.minimum(y0 + 1, h9 - 1)
        ax = fx - x0; ay = fy - y0
        T[py, px] = (field[y0, x0] * (1 - ax) * (1 - ay) + field[y0, x1] * ax * (1 - ay)
                     + field[y1, x0] * (1 - ax) * ay + field[y1, x1] * ax * ay)
    return T


def plateau_count(T, owner, min_px=PLATEAU_MIN_PX):
    """Plateaus: connected regions (4096, per stroke) whose reveal frame is exactly one value (to 1e-6 frame) and that are
    at least min_px pixels (8 px at 960, O1's A1 cut) - each inks at one instant."""
    q = np.where(np.isnan(T), -1, np.rint(T * 1e6)).astype(np.int64)
    regions, pixels, largest = 0, 0, 0
    for sid in np.unique(owner[owner > 0]):
        m = owner == sid
        vals, counts = np.unique(q[m], return_counts=True)
        for v in vals[counts >= min_px]:
            lab, _ = ndimage.label(m & (q == v))
            sizes = np.bincount(lab.ravel())[1:]
            sizes = sizes[sizes >= min_px]
            regions += int(sizes.size)
            pixels += int(sizes.sum())
            largest = max(largest, int(sizes.max()) if sizes.size else 0)
    return {"regions": regions, "pixels_4096": pixels, "largest_pixels_4096": largest, "inked_pixels_4096": int((owner > 0).sum()), "min_px": min_px}


def resize_premultiplied(img, size):
    """RGBA straight -> premultiplied Lanczos resize -> straight (no dark fringes)."""
    return img.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


def bleed_rgb(img, passes=8):
    """Fill the RGB of alpha-0 texels from inked neighbours (bilinear safety at the edge; alpha untouched)."""
    a = np.asarray(img).astype(np.float64)
    rgb, alpha = a[..., :3].copy(), a[..., 3]
    known = alpha > 0
    for _ in range(passes):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(alpha.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                k = np.roll(np.roll(known, dy, 0), dx, 1)
                acc += np.roll(np.roll(rgb, dy, 0), dx, 1) * k[..., None]
                cnt += k
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][..., None]
        known = known | grow
    if (~known).any():
        rgb[~known] = rgb[known].mean(axis=0) if known.any() else 0
    out = np.dstack([rgb, alpha]).round().clip(0, 255).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def crop_scale(img, box, long_edge):
    c = img.crop(box)
    w, h = c.size
    s = long_edge / float(max(w, h))
    size = (max(1, int(round(w * s))), max(1, int(round(h * s))))
    return bleed_rgb(resize_premultiplied(c, size)), size


def pad4(sprite):
    """K9 (S3-23): transparent padding to a multiple of 4 on each edge (BC7 blocks), split left/right (top/bottom) - the art
    stays where it was; returns the padded sprite and the (left, top, right, bottom) texels added."""
    w, h = sprite.size
    pw, ph = (-w) % 4, (-h) % 4
    if pw == 0 and ph == 0:
        return sprite, (0, 0, 0, 0)
    left, top = pw // 2, ph // 2
    canvas = Image.new("RGBA", (w + pw, h + ph), (0, 0, 0, 0))
    canvas.paste(sprite, (left, top))
    return bleed_rgb(canvas), (left, top, pw - left, ph - top)


def main():
    if "--previews" in sys.argv:
        os.makedirs(PREV, exist_ok=True)
    report = {"inputs": {}, "parameters": {}, "outputs": {}, "checks": {}, "placement": {}}
    for key, path in FILES.items():
        report["inputs"][key] = {"path": REPORT_PATHS.get(key, path), "sha256": sha256(path)}

    timeline = json.load(open(FILES["timeline"]))
    spec = json.load(open(FILES["motion_spec"]))
    assert spec["wordmark"]["window"] == [3, 47], spec["wordmark"]["window"]
    assert timeline["canvas"] == [SRC_W, SRC_H]
    w0, w1 = WINDOW

    # ------------------------------------------------ owner from the 36 masks, cross-checked with the owner map
    owner = np.zeros((SRC_H, SRC_W), np.int32)
    mask_files = []
    for s in timeline["strokes"]:
        path = os.path.join(STROKES, s["mask"])
        m = np.asarray(Image.open(path))
        assert m.shape == (SRC_H, SRC_W)
        assert not ((m > 0) & (owner > 0)).any(), "two masks claim one pixel: " + s["mask"]
        owner[m > 0] = s["id"]
        mask_files.append({"mask": s["mask"], "sha256": sha256(path), "owned": int((m > 0).sum())})
        assert int((m > 0).sum()) == s["owned_pixels"], s["mask"]
    report["inputs"]["stroke_masks"] = mask_files
    owner_ref = np.asarray(Image.open(FILES["owner"])).astype(np.int32)
    report["checks"]["owner_from_masks_equals_owner_map"] = bool((owner == owner_ref).all())
    assert report["checks"]["owner_from_masks_equals_owner_map"]

    progress = np.asarray(Image.open(FILES["progress"])).astype(np.float64) / 65535.0
    master_a = np.asarray(Image.open(FILES["master_4096"]).getchannel("A")).astype(np.float64) / 255.0
    report["checks"]["owner_covers_master_alpha"] = bool(((master_a > 0) == (owner > 0)).all())

    # ------------------------------------------------ per-pixel reveal frame
    T_v002 = np.full((SRC_H, SRC_W), np.nan)
    for s in timeline["strokes"]:
        sel = owner == s["id"]
        cp = s["curve"]["control_points"]
        frac = time_fraction(progress[sel], cp)
        T_v002[sel] = s["start_frame"] + (s["end_frame"] - s["start_frame"]) * frac
    T = repaired_times(timeline, owner) if REPAIRED_PROGRESS else T_v002
    report["checks"]["reveal_frames"] = [float(np.nanmin(T)), float(np.nanmax(T))]
    report["checks"]["plateaus"] = {"repaired_progress": REPAIRED_PROGRESS,
                                    "v002_progress": plateau_count(T_v002, owner),
                                    "used": plateau_count(T, owner)}

    # v003 frame: shift right by V003_SHIFT_X (the canvas edges are empty).
    T = np.roll(T, V003_SHIFT_X, axis=1)
    wgt = np.roll(master_a, V003_SHIFT_X, axis=1)

    # ------------------------------------------------ FACTOR x FACTOR alpha-weighted mean -> OUT_W x OUT_H
    Tb = np.nan_to_num(T, nan=0.0).reshape(OUT_H, FACTOR, OUT_W, FACTOR)
    Wb = (wgt * ~np.isnan(T)).reshape(OUT_H, FACTOR, OUT_W, FACTOR)
    wsum = Wb.sum(axis=(1, 3))
    tmean = np.where(wsum > 0, (Tb * Wb).sum(axis=(1, 3)) / np.maximum(wsum, 1e-9), np.nan)
    value = np.where(np.isnan(tmean), np.nan, np.clip(np.floor((tmean - w0) / (w1 - w0) * 254.0 + 1e-9), 0, 254))

    # dilation: inked neighbours' mean for DILATE rings
    known = ~np.isnan(value)
    v = np.nan_to_num(value, nan=0.0)
    for _ in range(DILATE):
        acc = np.zeros_like(v)
        cnt = np.zeros_like(v)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                k = np.roll(np.roll(known, dy, 0), dx, 1)
                acc += np.roll(np.roll(v, dy, 0), dx, 1) * k
                cnt += k
        grow = (~known) & (cnt > 0)
        v[grow] = np.floor(acc[grow] / cnt[grow] + 1e-9)
        known = known | grow
    timemap = np.where(known, np.clip(v, 0, 254), 255).astype(np.uint8)

    # ------------------------------------------------ colour art (v003 8K -> OUT_W x OUT_H)
    art8 = Image.open(FILES["wordmark_8192"]).convert("RGBA")
    assert art8.size == (8192, 3072)
    wordmark = bleed_rgb(resize_premultiplied(art8, (OUT_W, OUT_H)), passes=16)
    wa = np.asarray(wordmark)[..., 3]

    # contract checks
    inked = wa > 0
    nb_ok = True
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            nb_ok &= bool((np.roll(np.roll(timemap, dy, 0), dx, 1)[inked] <= 254).all())
    report["checks"]["timemap"] = {
        "size": [OUT_W, OUT_H],
        "ink_texels_le_254": bool((timemap[inked] <= 254).all()),
        "ink_neighbours_le_254": nb_ok,
        "never_texels_255": int((timemap == 255).sum()),
        "never_fraction": float((timemap == 255).mean()),
        "border_all_255": bool((timemap[0, :] == 255).all() and (timemap[-1, :] == 255).all() and (timemap[:, 0] == 255).all() and (timemap[:, -1] == 255).all()),
        "min": int(timemap.min()),
        "max_inked": int(timemap[timemap < 255].max()),
        "first_ink_frame": float(np.nanmin(T)),
        "last_ink_frame": float(np.nanmax(T)),
        "alpha_mismatch_texels": int(((wa > 0) & (timemap == 255)).sum()),
    }
    assert report["checks"]["timemap"]["ink_texels_le_254"] and nb_ok

    # A8 contract file: LA PNG (L = A = time; Unity imports it Alpha8, the shader reads .a), no ICC / gamma chunk.
    Image.fromarray(np.dstack([timemap, timemap]), "LA").save(os.path.join(OUT, NAMES["timemap"]), optimize=True)
    wordmark.save(os.path.join(OUT, NAMES["wordmark"]), optimize=True)

    # halo (loading only, over the key art): white + alpha, 1 within HALO_FULL texels of the ink, 0 from HALO_ZERO, clipped to
    # where the time map inks (the halo reveals with its letters through the same map); half resolution (it is soft).
    ink = wa > 64
    dist = ndimage.distance_transform_edt(~ink)
    halo = 1.0 - smoothstep(HALO_FULL, HALO_ZERO, dist)
    halo[timemap == 255] = 0.0
    halo_img = Image.fromarray(np.dstack([np.full(halo.shape, 255, np.uint8)] * 3 + [np.round(halo * 255).astype(np.uint8)]), "RGBA")
    halo_full_img = halo_img
    halo_img = halo_img.resize((HALO_W, HALO_H), Image.LANCZOS)
    halo_img = Image.fromarray(np.dstack([np.full((HALO_H, HALO_W), 255, np.uint8)] * 3 + [np.asarray(halo_img)[..., 3]]), "RGBA")
    halo_img.save(os.path.join(OUT, NAMES["halo"]), optimize=True)
    edge_band = (dist > 0) & (dist <= 4.0)
    # K8: the half-resolution halo against the full one, bilinear back up to 2048 (what the GPU does at the 2048 rect)
    full_a = np.asarray(halo_full_img)[..., 3].astype(np.float64) / 255.0
    up_a = np.asarray(halo_img.resize((OUT_W, OUT_H), Image.BILINEAR))[..., 3].astype(np.float64) / 255.0
    report["checks"]["halo"] = {"alpha_within_4_texels_min": float(halo[edge_band].min()), "alpha_beyond_dilate": float(halo[timemap == 255].max()),
                                "halo_1024_vs_2048": {"max_abs_alpha": float(np.abs(full_a - up_a).max()), "mean_abs_alpha": float(np.abs(full_a - up_a).mean()),
                                                      "p999_abs_alpha": float(np.percentile(np.abs(full_a - up_a), 99.9))}}

    # ------------------------------------------------ seal (emblem dark_safe 4096 -> square crop -> SEAL_TEX)
    emblem = Image.open(FILES["emblem_4096"]).convert("RGBA")
    eb = emblem.getchannel("A").getbbox()
    ecx, ecy = (eb[0] + eb[2]) / 2.0, (eb[1] + eb[3]) / 2.0
    side = max(eb[2] - eb[0], eb[3] - eb[1]) * SEAL_CROP_PAD
    sbox = (int(round(ecx - side / 2)), int(round(ecy - side / 2)), int(round(ecx + side / 2)), int(round(ecy + side / 2)))
    side = sbox[2] - sbox[0]
    seal = bleed_rgb(resize_premultiplied(emblem.crop(sbox), (SEAL_TEX, SEAL_TEX)))
    seal.save(os.path.join(OUT, NAMES["seal"]), optimize=True)

    # seal backing: the silhouette's convex hull (the dark paper inside the broken ring, as the delivered frames show it), 1 within BACK_FULL texels, 0 from BACK_ZERO; 256 (it is soft) - over the key art only.
    sa = np.asarray(seal)[..., 3] > 64
    # the ring is broken by the crack (no closed hole to fill): the silhouette's convex hull is the seal's paper
    ys, xs = np.nonzero(sa)
    pts = np.stack([xs, ys], axis=1).astype(np.float64)
    hull = ConvexHull(pts)
    hull_img = Image.new("L", (SEAL_TEX, SEAL_TEX), 0)
    ImageDraw.Draw(hull_img).polygon([tuple(pts[v]) for v in hull.vertices], fill=255)
    filled = np.asarray(hull_img) > 127
    bdist = ndimage.distance_transform_edt(~filled)
    back = 1.0 - smoothstep(BACK_FULL, BACK_ZERO, bdist)
    back_img = Image.fromarray(np.round(back * 255).astype(np.uint8)).resize((256, 256), Image.LANCZOS)
    Image.fromarray(np.dstack([np.full((256, 256), 255, np.uint8)] * 3 + [np.asarray(back_img)]), "RGBA").save(os.path.join(OUT, NAMES["backing"]), optimize=True)
    report["checks"]["seal_backing"] = {"filled_fraction": float(filled.mean()), "inside_ring_filled": bool(filled[SEAL_TEX // 2, SEAL_TEX // 2])}
    spec_per_emblem = SPEC_SEAL_LAYER / 4096.0
    report["placement"]["seal"] = {
        "emblem_ink_box": list(eb), "crop_box": list(sbox),
        "sprite_spec_px": side * spec_per_emblem,
        "sprite_centre_offset_spec_px": [((sbox[0] + sbox[2]) / 2.0 - 2048.0) * spec_per_emblem, -((sbox[1] + sbox[3]) / 2.0 - 2048.0) * spec_per_emblem],
        "ink_height_spec_px": (eb[3] - eb[1]) * spec_per_emblem,
        "ink_width_spec_px": (eb[2] - eb[0]) * spec_per_emblem,
        "layer_bottom_below_centre_spec_px": SPEC_SEAL_LAYER / 2.0,
    }

    # layers canvas -> spec px through the seal's ink box (layers seal = body + ring)
    body = np.asarray(Image.open(FILES["seal_body"]).getchannel("A"))
    ringl = np.asarray(Image.open(FILES["seal_ring"]).getchannel("A"))
    lb = Image.fromarray(np.maximum(body, ringl)).getbbox()
    lcx, lcy = (lb[0] + lb[2]) / 2.0, (lb[1] + lb[3]) / 2.0
    k_layers = ((eb[2] - eb[0]) / float(lb[2] - lb[0]) + (eb[3] - eb[1]) / float(lb[3] - lb[1])) / 2.0 * spec_per_emblem
    report["placement"]["layers_to_spec_px"] = k_layers
    report["placement"]["layers_seal_ink_box"] = list(lb)

    def fx(key, name):
        img = Image.open(FILES[key]).convert("RGBA")
        bb = img.getchannel("A").getbbox()
        pad = 8
        box = (max(0, bb[0] - pad), max(0, bb[1] - pad), min(img.width, bb[2] + pad), min(img.height, bb[3] + pad))
        sprite, size = crop_scale(img, box, FX_TEX)
        sprite, pad = pad4(sprite)
        sprite.save(os.path.join(OUT, name), optimize=True)
        # spec px per sprite texel; the padded sprite's rect grows by the pad, its centre moves by half the pad difference
        k = (box[2] - box[0]) * k_layers / size[0]
        cx, cy = (box[0] + box[2]) / 2.0, (box[1] + box[3]) / 2.0
        dx, dy = (pad[2] - pad[0]) * 0.5 * k, -(pad[3] - pad[1]) * 0.5 * k
        return {
            "source_box": list(box), "art_size": list(size), "sprite_size": list(sprite.size), "pad_ltrb": list(pad),
            "size_spec_px": [sprite.size[0] * k, sprite.size[1] * k],
            "centre_offset_spec_px": [(cx - lcx) * k_layers + dx, -(cy - lcy) * k_layers + dy],
        }

    report["placement"]["glow"] = fx("seal_glow", NAMES["glow"])
    report["placement"]["blot"] = fx("seal_ink_leak", NAMES["blot"])

    atlas = Image.open(FILES["ash_atlas"]).convert("RGBA")
    ring, rsize = crop_scale(atlas, RING_BOX, RING_TEX)
    ring, rpad = pad4(ring)
    ring.save(os.path.join(OUT, NAMES["ring"]), optimize=True)
    # the motion spec sizes the ring's art box (720 x 752 source); the padded sprite is drawn that much wider
    report["placement"]["ring"] = {"source_box": list(RING_BOX), "art_size": list(rsize), "sprite_size": list(ring.size), "pad_ltrb": list(rpad),
                                   "sprite_per_art": [ring.size[0] / float(rsize[0]), ring.size[1] / float(rsize[1])],
                                   "centre_offset_art": [(rpad[2] - rpad[0]) * 0.5 / rsize[0], -(rpad[3] - rpad[1]) * 0.5 / rsize[1]],
                                   "size_start_spec_px": spec["closed_ink_ring"]["size_start"],
                                   "size_end_spec_px": spec["closed_ink_ring"]["size_end"]}

    # ------------------------------------------------ outputs
    total = 0
    for key, name in NAMES.items():
        path = os.path.join(OUT, name)
        im = Image.open(path)
        size = os.path.getsize(path)
        total += size
        report["outputs"][key] = {"file": name, "size": list(im.size), "mode": im.mode, "bytes": size, "sha256": sha256(path)}
    report["outputs"]["total_bytes"] = total

    report["parameters"] = {
        "out": [OUT_W, OUT_H], "src": [SRC_W, SRC_H], "factor": FACTOR, "v003_shift_x_4096": V003_SHIFT_X,
        "window_frames": list(WINDOW), "window_seconds": [(w0 - 1) / 24.0, (w1 - 1) / 24.0],
        "encode": "floor((T - 3) / 44 * 254), 255 never; shader vis = saturate(p * 255 - code)", "dilate_texels": DILATE,
        "front_softness_spec": spec["wordmark"]["front_softness"], "seal_tex": SEAL_TEX, "seal_crop_pad": SEAL_CROP_PAD,
        "fx_tex": FX_TEX, "ring_tex": RING_TEX, "ring_box": list(RING_BOX), "resample": "premultiplied LANCZOS, RGB bled 8 (wordmark 16) texels into alpha 0",
        "halo_full_zero_texels_2048": [HALO_FULL, HALO_ZERO], "repaired_progress_960": REPAIRED_PROGRESS, "halo_size": [HALO_W, HALO_H], "fx_pad": "transparent to a multiple of 4 per edge",
        "seal_backing_full_zero_texels_768": [BACK_FULL, BACK_ZERO], "seal_backing_size": [256, 256],
    }

    report["motion"] = motion_tables(spec, timeline)
    validate(report, timemap, wordmark)
    report["bake_script_sha256"] = sha256(os.path.abspath(__file__))
    json.dump(report, open(os.path.join(OUT, "bake-report.json"), "w"), indent=1, sort_keys=True)
    print(json.dumps({k: report["outputs"][k] for k in report["outputs"]}, indent=1))
    print(json.dumps(report["checks"], indent=1))
    if "--install" in sys.argv:
        install(report)


def motion_tables(spec, timeline):
    """The motion-spec numbers the runtime reproduces (UiLogoTimeline's constants are tested against these)."""
    seal = spec["seal"]
    samples = []
    for f in range(seal["first_frame"], seal["squash_frames"][1] + 1):
        smp = seal["scale_samples"][str(f)]
        samples.append([f, float(smp["y"]), float(smp["sx"]), float(smp["sy"]), float(smp["glow"])])
    shake = [[int(k[len("frame"):])] + [float(v) for v in spec["camera_shake"][k]] for k in sorted(spec["camera_shake"]) if k.startswith("frame")]
    return {
        "fps": spec["fps"], "frame_base": spec["frame_base"], "intro_frames": spec["intro_frames"], "loop_frames": spec["loop_frames"],
        "window": spec["wordmark"]["window"], "front_softness": spec["wordmark"]["front_softness"],
        "last_stroke_end_frame": timeline["last_stroke_end_frame"],
        "seal_first_frame": seal["first_frame"], "seal_drop_end_frame": seal["drop_end_frame"], "seal_rest_bottom_y": 648.0,
        "seal_samples_frame_y_sx_sy_glow": samples,
        "ink_frames": spec["ink"]["frames"], "ink_alpha": spec["ink"]["alpha_formula"], "ink_scale": spec["ink"]["scale_formula"],
        "ring_frames": spec["closed_ink_ring"]["impact_frames"], "ring_alpha": spec["closed_ink_ring"]["alpha"],
        "ring_size_start": spec["closed_ink_ring"]["size_start"], "ring_size_end": spec["closed_ink_ring"]["size_end"],
        "ring_loop_breath_alpha_peak": spec["closed_ink_ring"]["loop_breath_alpha_peak"],
        "loop_glow_peak_alpha": spec["loop_glow"]["peak_alpha"], "camera_shake_frame_dx_dy": shake,
        "ash_sprites": spec["ash"]["sprites"], "ash_loop_drift_y": spec["ash"]["loop_drift_y_pixels"], "sparks": spec["sparks"]["sprites"],
        "canonical_hold": spec["canonical_hold"],
        "composition_spec_px": {"canvas": [1920, 1080], "seal_layer": [675, 78, 570, 570], "wordmark_rect": [480, 638, 960, 360]},
    }


def install(report):
    """Copy the runtime PNGs into the Unity project and write UI/Art/logo-intake.json (deterministic: sorted keys)."""
    repo = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
    files = []
    for key in LOGO_KEYS + MARK_KEYS:
        folder = LOGO_DIR if key in LOGO_KEYS else MARK_DIR
        os.makedirs(os.path.join(repo, folder), exist_ok=True)
        src = os.path.join(OUT, NAMES[key])
        dst = os.path.join(repo, folder, NAMES[key])
        shutil.copyfile(src, dst)
        out = report["outputs"][key]
        files.append({"role": key, "file": NAMES[key], "folder": folder.split("/Resources/")[1], "class": "Logo" if key in LOGO_KEYS else "UI1024",
                      "size": out["size"], "mode": out["mode"], "bytes": out["bytes"], "sha256": out["sha256"]})
    intake = {
        "schema": "dd-logo-intake-v1",
        "package": "AP-035",
        "bake": {"script": "tools/art/ap035-intake/bake_ap035_logo.py", "sha256": report["bake_script_sha256"],
                 "inputs": {k: {"path": v["path"].replace(K, "K"), "sha256": v["sha256"]} for k, v in report["inputs"].items() if isinstance(v, dict)},
                 "parameters": report["parameters"]},
        "files": files,
        "timemap": report["checks"]["timemap"],
        "placement": report["placement"],
        "motion": report["motion"],
    }
    with open(os.path.join(repo, INTAKE_JSON), "w") as f:
        json.dump(intake, f, indent=1, sort_keys=True)
        f.write("\n")
    print("installed", len(files), "files +", INTAKE_JSON)


def validate(report, timemap, wordmark):
    """Per-frame reveal vs the delivered lossless intro PNGs (v003 LoadingLogo/PNG/intro), wordmark rect [480, 638, 960, 360]."""
    blank = np.asarray(Image.open(EX + "/v001/LoadingLogo/blank_paper_frame.png").convert("RGB")).astype(np.int32)
    wm = wordmark.resize((960, 360), Image.BILINEAR)
    tm = Image.fromarray(timemap.astype(np.float32), "F").resize((960, 360), Image.BILINEAR)   # bilinear on the codes, as the GPU
    wa = np.asarray(wm)[..., 3].astype(np.float64) / 255.0
    tv = np.asarray(tm).astype(np.float64)
    rows = []
    for f in (4, 6, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 46):
        v4 = EX + "/v004/LoadingLogo/PNG/intro/frame_%04d.png" % f   # O1 A1 repaired frames (4..45), else v003
        fr = np.asarray(Image.open(v4 if os.path.exists(v4) else EX + "/v003/LoadingLogo/PNG/intro/frame_%04d.png" % f).convert("RGB")).astype(np.int32)
        d = np.abs(fr - blank).sum(axis=2)[638:998, 480:1440]
        truth = d > 60
        # the shader (DD_UiFxLogo.hlsl): p = reveal progress at the frame's PTS, vis = saturate(p * 255 - code); drawn where vis >= 0.5
        p = min(1.0, max(0.0, (f - WINDOW[0]) / (WINDOW[1] - WINDOW[0])))
        vis = np.clip(p * 255.0 - tv, 0.0, 1.0) * (tv < 254.5)
        row = {"frame": f, "truth_px": int(truth.sum())}
        for label, cut in (("a050", 0.5), ("a011", 0.11)):   # a011 ~ the truth's own cut (d > 60 over the paper)
            ours = (wa > cut) & (vis >= 0.5)
            union = (truth | ours).sum()
            row["iou_" + label] = float((truth & ours).sum() / union) if union else 1.0
            row["ours_px_" + label] = int(ours.sum())
        rows.append(row)
    report["checks"]["reveal_vs_intro_png"] = rows

    # readability: the OUT_W art bilinear-resampled to the Mac menu size vs the 8K master straight down to it
    art8 = Image.open(FILES["wordmark_8192"]).convert("RGBA")
    for label, width in (("1080_loading_846", 846), ("1080_menu_960", 960), ("mac_menu_1814", 1814)):
        h = int(round(width * 3 / 8.0))
        ref = np.asarray(resize_premultiplied(art8, (width, h)))[..., 3].astype(np.float64) / 255.0
        ours = np.asarray(wordmark.resize((width, h), Image.BILINEAR))[..., 3].astype(np.float64) / 255.0
        gy, gx = np.gradient(ref)
        gy2, gx2 = np.gradient(ours)
        report["checks"].setdefault("readability", {})[label] = {
            "alpha_mae": float(np.abs(ref - ours).mean()),
            "edge_sharpness_ratio": float(np.hypot(gx2, gy2).mean() / max(np.hypot(gx, gy).mean(), 1e-9)),
            "iou_at_0_5": float(((ref > 0.5) & (ours > 0.5)).sum() / max(((ref > 0.5) | (ours > 0.5)).sum(), 1)),
        }
        if label == "mac_menu_1814" and "--previews" in sys.argv:
            bg = Image.new("RGBA", (width, h), (20, 22, 28, 255))
            a = Image.alpha_composite(bg, wordmark.resize((width, h), Image.BILINEAR)).crop((0, 0, 900, 340))
            b = Image.alpha_composite(bg, resize_premultiplied(art8, (width, h))).crop((0, 0, 900, 340))
            sheet = Image.new("RGB", (900, 690), (255, 0, 255))
            sheet.paste(a.convert("RGB"), (0, 0))
            sheet.paste(b.convert("RGB"), (0, 350))
            sheet.save(os.path.join(PREV, "readability_mac_1814_ours_top_master_bottom.png"))


if __name__ == "__main__":
    sys.exit(main())
