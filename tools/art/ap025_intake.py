#!/usr/bin/env python3
"""WP-084 (S-03, C-00 05.10) · stage an AP-025 export into the Unity project, driven by tools/art/ap025-intake.json.

    python3 tools/art/ap025_intake.py --source <K>/asset-production/AP-025/exports/v002 [--project <repo>] [--dry-run]

Re-runnable: a new export version (v003 …) is one command with another --source; the mapping is data (ap025-intake.json).
Steps: verify every used source against the export's SHA256SUMS → copy / rename → build the 9-slice mask flipbook (3×3,
row-major from the top-left = Unity's Texture2DArray flipbook slice order) → retire the replaced legacy files → write
Zombie/AP025-intake-manifest.json (export version, source + output SHA-256). Unity then imports and the art pipeline
(ArtPipelineMenu.RunCharacters + ArtTierBuilder.BuildAll) rebuilds libraries, materials and the prefab.
"""
import argparse, glob, hashlib, json, os, shutil, sys

ZOMBIE = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Characters/Zombie"


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def load_sums(src):
    sums = {}
    path = os.path.join(src, "SHA256SUMS")
    if not os.path.exists(path):
        sys.exit("SHA256SUMS missing in " + src)
    for line in open(path, encoding="utf-8"):
        parts = line.strip().split(None, 1)
        if len(parts) == 2:
            sums[parts[1].lstrip("*").lstrip("./")] = parts[0].lower()
    return sums


def verify(src, rel, sums):
    if rel not in sums:
        sys.exit("not in SHA256SUMS: " + rel)
    got = sha256(os.path.join(src, rel))
    if got != sums[rel]:
        sys.exit("SHA-256 mismatch: %s (%s != %s)" % (rel, got, sums[rel]))
    return got


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True, help="AP-025 export folder (e.g. …/exports/v002)")
    ap.add_argument("--project", default=os.path.abspath(os.path.join(here, "..", "..")), help="repo root")
    ap.add_argument("--map", default=os.path.join(here, "ap025-intake.json"))
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    src = os.path.abspath(a.source)
    dst = os.path.join(a.project, ZOMBIE)
    spec = json.load(open(a.map, encoding="utf-8"))
    sums = load_sums(src)
    out = {"schema": "DD.ap025-intake-manifest.v1", "export": os.path.basename(src.rstrip("/")), "map": os.path.basename(a.map),
           "builder": spec["builder"], "copied": [], "maskArray": None, "retired": []}

    # two phases (3d-modeller 06.10): every source is verified against SHA256SUMS first, so a bad file stops the intake
    # before anything is copied — Assets never hold a v002 / v003 mix
    plan = []

    def put(rel, target_rel):
        s = verify(src, rel, sums)
        out["copied"].append({"from": rel, "to": target_rel, "sha256": s})
        plan.append((rel, target_rel))

    for item in spec["copy"]:
        if "glob" in item:
            matches = sorted(glob.glob(os.path.join(src, item["glob"])))
            if not matches:
                sys.exit("no source matches " + item["glob"])
            for m in matches:
                rel = os.path.relpath(m, src)
                put(rel, os.path.join(item["toDir"], os.path.basename(m)))
        else:
            put(item["from"], item["to"])

    ma = spec["maskArray"]
    cols, rows = ma["grid"]
    if len(ma["slices"]) != cols * rows:
        sys.exit("maskArray: %d slices for a %dx%d grid" % (len(ma["slices"]), cols, rows))
    for rel in ma["slices"]:
        verify(src, rel, sums)
    if not a.dry_run:
        for rel, target_rel in plan:
            target = os.path.join(dst, target_rel)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copyfile(os.path.join(src, rel), target)
    from PIL import Image
    tiles = []
    for rel in ma["slices"]:
        im = Image.open(os.path.join(src, rel))
        if im.mode not in ("L", "I;16"):
            im = im.convert("L")
        tiles.append(im)
    w, h = tiles[0].size
    if any(t.size != (w, h) for t in tiles):
        sys.exit("maskArray: slices differ in size")
    sheet = Image.new("L", (w * cols, h * rows), 0)
    for i, t in enumerate(tiles):
        sheet.paste(t.convert("L"), ((i % cols) * w, (i // cols) * h))
    target = os.path.join(dst, ma["to"])
    if not a.dry_run:
        os.makedirs(os.path.dirname(target), exist_ok=True)
        sheet.save(target, optimize=True)
    out["maskArray"] = {"to": ma["to"], "grid": [cols, rows], "slice": [w, h], "slices": ma["slices"],
                        "sha256": None if a.dry_run else sha256(target)}

    for rel in spec.get("retire", []):
        p = os.path.join(dst, rel)
        for q in (p, p + ".meta"):
            if os.path.exists(q):
                out["retired"].append(os.path.relpath(q, dst))
                if not a.dry_run:
                    os.remove(q)

    if not a.dry_run:
        with open(os.path.join(dst, "AP025-intake-manifest.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, indent=1, ensure_ascii=False)
            f.write("\n")
    print("AP-025 intake %s: %d copied, mask array %dx%d slices of %dx%d, %d retired%s"
          % (out["export"], len(out["copied"]), cols, rows, w, h, len(out["retired"]), " (dry run)" if a.dry_run else ""))


if __name__ == "__main__":
    main()
