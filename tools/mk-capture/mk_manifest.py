#!/usr/bin/env python3
"""WP-110 (S-01 mk-capture) · the label's record: capture.json (what was asked, what came out, per segment) + SHA256SUMS over the
deliverables (MP4s, stills, clips.json / clip.json / summary.json / events.tsv; raw JPEG frames and the WAV excluded).

usage: mk_manifest.py <label dir> --app A --tier T --formats F --guard N --rc N --signal N
       mk_manifest.py gate --app A --label L [--final-sha SHA]   (C-00 12.10: the R4 FINAL gate, before anything opens)

The gate (C-00 12.10, the user: the Instagram pictures are R4 as finished): a shoot runs only on a build of the R4 FINAL PASS
commit. The FINAL SHA is --final-sha, else DD_R4_FINAL_SHA, else the first word of K/wp-110/FINAL-SHA.txt — the source S-11's
ig_gate.py reads. The build's commit (report.json head, else build-report.json stamp.commit) must be the same commit (≥ 7 hex
digits, prefix). A label starting with "dry" is a dry run: exempt, and recorded dry (ig_gate never lets one into final/).
Prints the gate record (JSON); exit 0 pass · 64 refused."""
import argparse
import hashlib
import json
import os
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dd_paths  # noqa: E402

MIN_SHA = 7

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


def final_sha(flag=None):
    """(sha, source): --final-sha, DD_R4_FINAL_SHA, K/wp-110/FINAL-SHA.txt (first word) — ig_gate.py's order after the flag."""
    if flag:
        return flag.strip().lower(), "flag"
    v = os.environ.get("DD_R4_FINAL_SHA", "").strip()
    if v:
        return v.lower(), "env"
    f = dd_paths.KPROJ / "wp-110" / "FINAL-SHA.txt"
    try:
        words = f.read_text(encoding="utf-8").split()
    except OSError:
        words = []
    return (words[0].lower(), str(f)) if words else (None, None)


def same_commit(a, b):
    if not a or not b or min(len(a), len(b)) < MIN_SHA:
        return False
    n = min(len(a), len(b))
    return a[:n].lower() == b[:n].lower()


def gate(app, label, flag=None):
    """(ok, record): the R4 FINAL gate of one capture (see the module doc)."""
    b = build_identity(app)
    head = str(b.get("head") or (b.get("stamp") or {}).get("commit") or "").lower()
    fin, source = final_sha(flag)
    dry = label.lower().startswith("dry")
    rec = {"dry": dry, "buildHead": head or None, "finalSha": fin, "finalShaSource": source}
    if dry:
        rec["verdict"] = "dry run — exempt, never published (ig_gate)"
        return True, rec
    if flag is not None and not re.fullmatch(r"[0-9a-fA-F]{7,40}", flag.strip()):
        rec["verdict"] = "--final-sha must be 7..40 hex digits (%s)" % flag
        return False, rec
    if not fin:
        rec["verdict"] = ("the R4 FINAL SHA is unknown (--final-sha, DD_R4_FINAL_SHA or %s) — a shoot runs only on the R4 FINAL "
                          "PASS build; a dry run's label starts with dry" % (dd_paths.KPROJ / "wp-110" / "FINAL-SHA.txt"))
        return False, rec
    if not same_commit(head, fin):
        rec["verdict"] = "the build is %s, the R4 FINAL is %s — build the FINAL commit (tools/build.sh dev on it)" % (
            head[:12] or "unknown", fin[:12])
        return False, rec
    rec["verdict"] = "R4 FINAL build"
    return True, rec


def deliverables(out):
    files = set()
    for pat in DELIVER:
        for p in out.rglob(pat):
            rel = p.relative_to(out).parts
            if p.is_file() and not any(part in ("frames", "frames-hud", "sandbox", "ug", "bot") or part.startswith("work") for part in rel):
                files.add(p)
    return sorted(files)


def main(argv):
    if argv[:1] == ["gate"]:
        g = argparse.ArgumentParser(prog="mk_manifest.py gate")
        g.add_argument("--app", required=True)
        g.add_argument("--label", required=True)
        g.add_argument("--final-sha")
        ga = g.parse_args(argv[1:])
        ok, rec = gate(ga.app, ga.label, ga.final_sha)
        print(json.dumps(rec))
        return 0 if ok else 64
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
              "build": build_identity(a.app), "finalGate": read_json(out / "logs" / "final-gate.json"),
              "dry": bool((read_json(out / "logs" / "final-gate.json") or {}).get("dry")), "tier": a.tier, "formats": a.formats.split(","), "segments": segments,
              "guardRestore": int(a.guard), "exit": int(a.rc), "signal": int(a.signal),
              "files": {str(p.relative_to(out)): sha(p) for p in files}}
    (out / "capture.json").write_text(json.dumps(record, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    (out / "SHA256SUMS").write_text("".join("%s  %s\n" % (h, f) for f, h in record["files"].items()), encoding="utf-8")
    print("capture.json: %d segments, %d files" % (len(segments), len(files)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
