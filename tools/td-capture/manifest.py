#!/usr/bin/env python3
"""WP-099 (S-01, C-00 07.10) · td-capture's record: <out>/capture.json (what was asked, the build, what came out, the
guard result) and <out>/SHA256SUMS (every stills/ and video/ file except the raw frames, which are listed by count).
usage: manifest.py <out> --app A --seed S --poses P --size WxH --tier T --hold 0|1 [--scenario NAME] --guard N --rc N --signal N"""
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
    rows = [(t, f) for t, f in rows if f > 0]   # 08.10: rows before the WAV's first sample (boot: 0 frames) are no clock
    if not rows:
        return None
    f0 = rows[0][1] - rows[0][0] * rate
    res = [((f - f0) / rate - t) for t, f in rows]
    return {"anchors": len(rows), "atStop": round(res[-1], 4), "maxAbs": round(max(abs(r) for r in res), 4)}


# C-00 08.10: a boot clip is judged per phase — the loading screen's fps and hitches are a product measurement, not a
# capture fault; the verdict comes from the menu and the run (fighting) only. A gameplay video (mode video) is one phase.
PHASES = (("loading", ("loadingStart",), ("loadingEnd",)),
          ("menu", ("menu",), ("selectionRequested", "selection", "runRequested", "fighting")),
          ("selection", ("selectionRequested", "selection"), ("fighting",)),
          ("fighting", ("fighting",), ()))
JUDGED_BOOT = ("menu", "fighting")


def frame_times(out):
    times = []
    try:
        for line in (out / "video" / "frames.tsv").read_text(encoding="utf-8").splitlines():
            cols = line.split("\t")
            try:
                times.append(float(cols[1]))
            except (IndexError, ValueError):
                continue
    except FileNotFoundError:
        pass
    return sorted(times)


def phase_stats(out, align):
    """[{name, start, end, fps, longestFrame, avError}] per phase (s from t0); boot: from sync.json phases, else one
    "fighting" stretch over the whole clip. avError = the worst aligned second starting inside the phase."""
    times = frame_times(out)
    if not times:
        return []
    end = times[-1]
    marks = (read_json(out / "video" / "sync.json") or {}).get("phases")
    first = lambda keys: next((marks[k] for k in keys if marks.get(k) is not None), None)
    spans = []
    if marks:
        for name, starts, ends in PHASES:
            st = first(starts) if not (name == "loading" and marks.get("loadingStart") is None) else 0.0
            en = first(ends) if ends else end
            if st is not None:
                spans.append((name, st, en if en is not None else end))
    else:
        spans = [("fighting", 0.0, end)]
    per = (align or {}).get("perSecond") or []
    stats = []
    for name, st, en in spans:
        if en <= st:
            continue
        inside = [t for t in times if st <= t < en or (en >= end and t == end)]   # the last phase keeps the last frame
        gaps = [b - a for a, b in zip(inside, inside[1:])]
        errs = [e for t, e in per if st <= t < en]
        stats.append({"name": name, "start": round(st, 3), "end": round(en, 3),
                      "fps": round(len(inside) / (en - st), 2), "longestFrame": round(max(gaps), 3) if gaps else None,
                      "avError": max(errs) if errs else None})
    return stats


def video_quality(out):
    """effective capture fps against MIN_FPS, the A/V error against MAX_AV_DRIFT, per phase, and the machine load each
    launch saw (logs/load-<name>.txt). verdict: "ok", or the issues joined by "+" (low-fps, av-drift, phase-missing) —
    judged on the menu + fighting phases of a boot clip, on the whole clip otherwise; None without a video."""
    summary = read_json(out / "video" / "summary.json") or {}
    fps = summary.get("effectiveFps")
    drift = av_drift(out)
    align = read_json(out / "video" / "align.json")   # align_audio.py: the WAV laid per 1 s anchor (the MP4's sound)
    loads = {p.stem[5:]: p.read_text().strip() for p in sorted((out / "logs").glob("load-*.txt"))}
    phases = phase_stats(out, align)
    boot = summary.get("mode") == "boot" or bool((read_json(out / "video" / "sync.json") or {}).get("phases"))
    # aligned: the MP4 is exact at every anchor, off by at most maxSecondErr inside a second; else the one-offset WAV
    # drifts with the audio clock
    av_err = align["maxSecondErr"] if align else (drift["maxAbs"] if drift else None)
    issues = []
    if boot:
        judged = [p for p in phases if p["name"] in JUDGED_BOOT]
        if {p["name"] for p in judged} != set(JUDGED_BOOT):
            issues.append("phase-missing")
        if any(p["fps"] < MIN_FPS for p in judged):
            issues.append("low-fps")
        if any((p["avError"] or 0) > MAX_AV_DRIFT for p in judged) or (not align and av_err is not None and av_err > MAX_AV_DRIFT):
            issues.append("av-drift")
    else:
        # gt SHOULD-3 (08.10): no summary.json → the measured stretch's fps, not a free "ok"
        measured = fps if fps is not None else (phases[0]["fps"] if phases else None)
        if measured is not None and measured < MIN_FPS:
            issues.append("low-fps")
        if av_err is not None and av_err > MAX_AV_DRIFT:
            issues.append("av-drift")
    verdict = None if fps is None and drift is None and not phases else ("+".join(issues) or "ok")
    return {"effectiveFps": fps, "minFps": MIN_FPS, "avDrift": drift, "avAlign": {k: v for k, v in (align or {}).items()
            if k != "perSecond"} or None, "avError": av_err, "maxAvDrift": MAX_AV_DRIFT, "phases": phases,
            "judgedOn": list(JUDGED_BOOT) if boot else ["whole clip"], "verdict": verdict, "loadAtLaunch": loads}


def warning_lines(q):
    """the timeline lines for a videoQuality record — every number from what the verdict was judged on (boot: the menu
    and fighting phases; else the whole clip). gt SHOULD-2 (08.10): no line may contradict its verdict or crash."""
    v = (q.get("verdict") or "").split("+")
    phases = q.get("phases") or []
    lines = []
    if phases:
        lines.append("td-capture: video phases (judged on %s): %s" % ("+".join(q.get("judgedOn") or []), ", ".join(
            "%s %.1f fps/longest %s s/av %s" % (p["name"], p["fps"], p["longestFrame"], p["avError"]) for p in phases)))
    judged = [p for p in phases if p["name"] in (q.get("judgedOn") or [])] or phases
    if "phase-missing" in v:
        lines.append("WARNING td-capture: the boot clip never reached the menu and/or the run — judged phases missing (exit 1)")
    if "low-fps" in v:
        if q.get("judgedOn") == ["whole clip"] and q.get("effectiveFps") is not None:   # what the verdict used
            what = "effective %.1f fps" % q["effectiveFps"]
        else:
            slow = [p for p in judged if p["fps"] < q.get("minFps", MIN_FPS)] or judged
            what = ", ".join("%s %.1f fps" % (p["name"], p["fps"]) for p in slow) or "fps unknown"
        lines.append("WARNING td-capture: the MP4 stutters — %s < %d (load at launch %s); retake on a quieter machine"
                     % (what, q.get("minFps", MIN_FPS), q.get("loadAtLaunch")))
    if "av-drift" in v:
        errs = [(p["avError"], p["name"]) for p in judged if p.get("avError") is not None]
        err, where = max(errs) if errs and q.get("judgedOn") != ["whole clip"] else (q.get("avError"), "the clip")
        clock = (q.get("avDrift") or {}).get("atStop")
        lines.append("WARNING td-capture: sound and picture are up to %s s apart in %s (> %.2f%s) — retake on a quieter machine"
                     % ("%.3f" % err if isinstance(err, (int, float)) else err, where, q.get("maxAvDrift", MAX_AV_DRIFT), "" if clock is None else "; audio clock %+.3f s by the stop" % clock))
    return lines


def _number(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)   # gt NOTE-1 (37daa37f): a bool is no second


def _video_end(out):
    """The last grabbed frame's t in video/frames.tsv (the MP4 runs to it), None without frames."""
    last = None
    try:
        for line in (out / "video" / "frames.tsv").read_text(encoding="utf-8").splitlines():
            cols = line.split("\t")
            try:
                last = max(last or 0.0, float(cols[1]))
            except (IndexError, ValueError):
                continue
    except OSError:
        return None
    return last


def scenario_record(out, name):
    """R4 kit D2 (S-10 / S-06 11.10): what the bot's scenario did, read from <out>/bot/scenario.json (QaRunBot writes
    completed, realtime stamps in Time.realtimeSinceStartupAsDouble — null = not reached — and counters / flags at the top
    level). Each realtime stamp is also given in video seconds (minus video/sync.json t0.realtimeSinceStartup — the same
    process clock). played = the file names the scenario asked for, completed is true, the video t0 is known and every
    reached stamp lies inside the video (0 … the last frame; gt SHOULD-1 37daa37f: a walk that began before t0 or ended
    after the last frame is not in the clip). None when no scenario was asked for."""
    if not name:
        return None
    path = out / "bot" / "scenario.json"
    s = read_json(path)
    if not isinstance(s, dict):
        why = ("bot/scenario.json unreadable or not a JSON object" if path.exists()
               else "bot/scenario.json missing — does this build's QaRunBot know -ddQaBotScenario %s?" % name)
        return {"scenario": name, "played": False, "reason": why}
    sync = read_json(out / "video" / "sync.json")
    t0obj = sync.get("t0") if isinstance(sync, dict) else None
    t0 = t0obj.get("realtimeSinceStartup") if isinstance(t0obj, dict) else None
    t0 = t0 if _number(t0) else None
    realtime = s.get("realtime") if isinstance(s.get("realtime"), dict) else {}
    rec = {k: v for k, v in s.items() if k != "realtime"}
    rec["realtime"] = realtime
    rec["video"] = {k: (round(v - t0, 3) if _number(v) and t0 is not None else None) for k, v in realtime.items()}
    rec["videoT0Realtime"] = t0
    end = _video_end(out)
    rec["videoEndSeconds"] = None if end is None else round(end, 3)
    reasons = []
    if s.get("scenario") != name:
        reasons.append("bot/scenario.json names %r, not %r" % (s.get("scenario"), name))
    if s.get("completed") is not True:
        reasons.append("completed %r" % (s.get("completed"),))
    if t0 is None:
        reasons.append("video t0 missing (video/sync.json t0.realtimeSinceStartup)")
    elif end is None:
        reasons.append("no video frames (video/frames.tsv)")
    else:
        outside = ["%s %.2f s" % (k, v) for k, v in rec["video"].items() if v is not None and not (0.0 <= v <= end)]
        if outside:
            reasons.append("outside the video 0..%.2f s: %s" % (end, ", ".join(outside)))
    rec["played"] = not reasons
    if reasons:
        rec["reason"] = "; ".join(reasons)
    return rec


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    for k in ("app", "seed", "poses", "size", "tier", "hold", "guard", "rc", "signal"):
        ap.add_argument("--" + k, required=True)
    ap.add_argument("--scenario", default="")   # R4 (S-06/S-01 11.10): the QaRunBot scenario of the video, "" = free fight
    a = ap.parse_args(argv)
    out = Path(a.out)
    files = sorted(p for sub in ("stills", "video") for p in (out / sub).rglob("*")
                   if p.is_file() and "frames" not in p.relative_to(out).parts)
    frames = sorted((out / "video" / "frames").glob("f-*")) if (out / "video" / "frames").is_dir() else []
    record = {
        "tool": "tools/td-capture.sh (WP-099)", "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "app": a.app, "build": build_identity(a.app), "seed": int(a.seed), "size": a.size, "tier": a.tier,
        "holdSpawnsForStills": a.hold == "1", "scenario": a.scenario or None,
        "scenarioStamps": scenario_record(out, a.scenario),
        "poses": {"file": a.poses, "sha256": sha(a.poses) if Path(a.poses).is_file() else None},
        "stills": read_json(out / "stills" / "poses.json"),
        "video": {"summary": read_json(out / "video" / "summary.json"), "probe": read_json(out / "video" / "probe.json"),
                  "sync": read_json(out / "video" / "sync.json"), "rawFrames": len(frames),
                  # C-00 08.10 (boot video): loadingStart / loadingEnd / menu / fighting, real s from t0 (null: not seen)
                  "phases": (read_json(out / "video" / "sync.json") or {}).get("phases")},
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
