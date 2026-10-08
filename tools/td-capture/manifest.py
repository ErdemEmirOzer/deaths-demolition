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
    # gt MUST-2 (08.10): tools/build.sh writes the stamp (git describe / commit / branch / UTC) into build-report.json and
    # its HEAD + app-manifest hash into report.json, next to the .app
    stamp = (read_json(Path(app).parent / "build-report.json") or {}).get("stamp")
    report = read_json(Path(app).parent / "report.json") or {}
    if stamp:
        info["stamp"] = stamp
    for k in ("head", "mode", "utc", "appManifestSha256"):
        if report.get(k) is not None:
            info[k] = report[k]
    return info


MIN_FPS = 24        # below this the gameplay MP4 stutters: the Player itself ran slow (a loaded shared machine) — retake
MAX_AV_DRIFT = 0.1  # s: beyond this the sound visibly leads / lags the picture somewhere in the MP4 — retake


def av_drift(out):
    """gt MUST-1 (08.10): the audio clock against real time, from video/audio-sync.tsv (one anchor per second:
    t, wav frames captured): residual(t) = (frames - frames@t0) / rate - t. The MP4 lays the WAV from t0 on, so a
    residual of -0.5 s means the sound runs 0.5 s ahead of the picture there. (summary.json's driftSeconds is read
    after the frame drain, not at the stop — misleading, not used.)"""
    sync = read_json(out / "video" / "sync.json") or {}
    rate = ((sync.get("audio") or {}).get("sampleRate")) or 48000
    rows = []
    try:
        for line in (out / "video" / "audio-sync.tsv").read_text(encoding="utf-8").splitlines():
            cols = line.split("\t")
            if line.startswith("#") or len(cols) < 2:
                continue
            try:
                rows.append((float(cols[0]), int(cols[1])))
            except ValueError:
                continue
    except FileNotFoundError:
        return None
    if not rows:
        return None
    f0 = rows[0][1] - rows[0][0] * rate
    res = [((f - f0) / rate - t) for t, f in rows]
    return {"anchors": len(rows), "atStop": round(res[-1], 4), "maxAbs": round(max(abs(r) for r in res), 4)}


def video_quality(out):
    """effective capture fps against MIN_FPS, the A/V drift against MAX_AV_DRIFT, and the machine load each launch
    saw (logs/load-<name>.txt). verdict: "ok", or the issues joined by "+" (low-fps, av-drift); None without a video."""
    summary = read_json(out / "video" / "summary.json") or {}
    fps = summary.get("effectiveFps")
    drift = av_drift(out)
    loads = {p.stem[5:]: p.read_text().strip() for p in sorted((out / "logs").glob("load-*.txt"))}
    issues = []
    if fps is not None and fps < MIN_FPS:
        issues.append("low-fps")
    if drift is not None and drift["maxAbs"] > MAX_AV_DRIFT:
        issues.append("av-drift")
    verdict = None if fps is None and drift is None else ("+".join(issues) or "ok")
    return {"effectiveFps": fps, "minFps": MIN_FPS, "avDrift": drift, "maxAvDrift": MAX_AV_DRIFT, "verdict": verdict,
            "loadAtLaunch": loads}


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
