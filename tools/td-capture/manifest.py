#!/usr/bin/env python3
"""WP-099 (S-01, C-00 07.10) · td-capture's record: <out>/capture.json (what was asked, the build, what came out, the
guard result) and <out>/SHA256SUMS (every stills/ and video/ file except the raw frames, which are listed by count).
usage: manifest.py <out> --app A --seed S --poses P --size WxH --tier T --hold 0|1 --guard N --rc N --signal N"""
import argparse
import hashlib
import json
import plistlib
import sys
import time
from pathlib import Path


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def read_json(p):
    try:
        return json.loads(Path(p).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def build_identity(app):
    """the bundle's version strings and any build-info file DdBuild writes next to it (the commit the build came from)."""
    info = {}
    try:
        with open(Path(app) / "Contents" / "Info.plist", "rb") as f:
            pl = plistlib.load(f)
        info = {k: pl.get(k) for k in ("CFBundleShortVersionString", "CFBundleVersion")}
    except (OSError, ValueError):
        pass
    for cand in (Path(app).parent / "build-info.json", Path(app).parent / "build.json"):
        data = read_json(cand)
        if data is not None:
            info["buildInfo"] = {"file": str(cand), "data": data}
            break
    return info


MIN_FPS = 24   # below this the gameplay MP4 stutters: the Player itself ran slow (a loaded shared machine) — retake


def video_quality(out):
    """effective capture fps against MIN_FPS, plus the machine load each launch saw (logs/load-<name>.txt)."""
    summary = read_json(out / "video" / "summary.json") or {}
    fps = summary.get("effectiveFps")
    loads = {p.stem[5:]: p.read_text().strip() for p in sorted((out / "logs").glob("load-*.txt"))}
    verdict = None if fps is None else ("ok" if fps >= MIN_FPS else "low-fps")
    return {"effectiveFps": fps, "minFps": MIN_FPS, "verdict": verdict, "loadAtLaunch": loads}


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    for k in ("app", "seed", "poses", "size", "tier", "hold", "guard", "rc", "signal"):
        ap.add_argument("--" + k, required=True)
    a = ap.parse_args(argv)
    out = Path(a.out)
    files = sorted(p for sub in ("stills", "video") for p in (out / sub).rglob("*")
                   if p.is_file() and "frames" not in p.relative_to(out).parts)
    frames = sorted((out / "video" / "frames").glob("f-*")) if (out / "video" / "frames").is_dir() else []
    record = {
        "tool": "tools/td-capture.sh (WP-099)", "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "app": a.app, "build": build_identity(a.app), "seed": int(a.seed), "size": a.size, "tier": a.tier,
        "holdSpawnsForStills": a.hold == "1",
        "poses": {"file": a.poses, "sha256": sha(a.poses) if Path(a.poses).is_file() else None},
        "stills": read_json(out / "stills" / "poses.json"),
        "video": {"summary": read_json(out / "video" / "summary.json"), "probe": read_json(out / "video" / "probe.json"),
                  "sync": read_json(out / "video" / "sync.json"), "rawFrames": len(frames)},
        "videoQuality": video_quality(out),
        "guardRestore": int(a.guard), "exit": int(a.rc), "signal": int(a.signal),
        "files": {str(p.relative_to(out)): sha(p) for p in files},
    }
    (out / "capture.json").write_text(json.dumps(record, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    (out / "SHA256SUMS").write_text("".join("%s  %s\n" % (h, f) for f, h in record["files"].items()), encoding="utf-8")
    print("capture.json: %d files, %d raw frames" % (len(files), len(frames)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
