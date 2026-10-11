#!/usr/bin/env python3
"""WP-110 (S-01 mk-capture) · the label's record: capture.json (what was asked, what came out, per segment) + SHA256SUMS over the
deliverables (MP4s, stills, clips.json / clip.json / summary.json / events.tsv; raw JPEG frames and the WAV excluded).

usage: mk_manifest.py <label dir> --app A --tier T --formats F --guard N --rc N --signal N"""
import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

DELIVER = ("*.mp4", "*.png", "*.jpg", "clips.json", "clip.json", "summary.json", "events.tsv", "sync.json", "segments.txt")


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def read_json(p):
    try:
        return json.loads(Path(p).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def build_identity(app):
    root = Path(app).parent
    rep = read_json(root / "build-report.json") or {}
    rj = read_json(root / "report.json") or {}
    return {"stamp": rep.get("stamp"), "head": rj.get("head"), "mode": rj.get("mode")}


def deliverables(out):
    files = set()
    for pat in DELIVER:
        for p in out.rglob(pat):
            rel = p.relative_to(out).parts
            if p.is_file() and not any(part in ("frames", "frames-hud", "sandbox", "ug", "bot") or part.startswith("work") for part in rel):
                files.add(p)
    return sorted(files)


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    for k in ("app", "tier", "formats", "guard", "rc", "signal"):
        ap.add_argument("--" + k, required=True)
    a = ap.parse_args(argv)
    out = Path(a.out)
    segments = []
    for seg in sorted(p for p in out.iterdir() if p.is_dir() and (p / "logs").is_dir() and p.name not in ("logs", "ug")):
        clips = read_json(seg / "clips.json") or {}
        summary = read_json(seg / "summary.json") or {}
        segments.append({"segment": seg.name, "stopReason": summary.get("stopReason"), "durationSeconds": summary.get("durationSeconds"),
                         "sessionFps": (summary.get("frames") or {}).get("sessionFps"), "clipsKept": summary.get("clipsKept"),
                         "events": summary.get("events"), "ring": summary.get("ring"), "hud": summary.get("hud"),
                         "videos": sum(len(c.get("videos") or []) for c in clips.get("clips") or []),
                         "stills": sum(len(c.get("stills") or []) for c in clips.get("clips") or []),
                         "clipsFailed": sum(1 for c in clips.get("clips") or [] if not c.get("ok")),
                         "error": read_json(seg / "error.json")})
    files = deliverables(out)
    record = {"tool": "tools/mk-capture.sh (WP-110)", "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "app": a.app,
              "build": build_identity(a.app), "tier": a.tier, "formats": a.formats.split(","), "segments": segments,
              "guardRestore": int(a.guard), "exit": int(a.rc), "signal": int(a.signal),
              "files": {str(p.relative_to(out)): sha(p) for p in files}}
    (out / "capture.json").write_text(json.dumps(record, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    (out / "SHA256SUMS").write_text("".join("%s  %s\n" % (h, f) for f, h in record["files"].items()), encoding="utf-8")
    print("capture.json: %d segments, %d files" % (len(segments), len(files)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
