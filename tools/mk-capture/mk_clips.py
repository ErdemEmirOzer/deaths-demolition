#!/usr/bin/env python3
"""WP-110 (S-01 mk-capture, C-00 11.10) · one segment's clips → Instagram-format MP4s + cropped stills + clips.json.

The capture agent (MkCaptureAgent) leaves <seg>/clips/NN/ with frames/ (HUD-less) and/or frames-hud/ (JPEG, 1080×1920 master),
frames.tsv (index, SESSION t), stills[-hud]/*.png and clip.json; the session's audio.wav, audio-sync.tsv (one WAV anchor per
second, session t) and sync.json sit in <seg>/. Per clip and stream this lays a work folder in td-capture's video-dir shape —
frames.tsv / audio-sync.tsv / sync.json rebased to the clip's first frame, audio.wav a link to the session WAV — so
tools/td-capture/align_audio.py places the sound second by second, then tdmux writes one MP4 per format (centre crops of the
master, never scaled up) and mp4probe checks it. Stills: PNG + JPEG q92 per format. When every MP4 of a clip probed
(H.264 + AAC, the duration of its frames) the clip's raw JPEG frames are removed — they are this tool's own intermediate
(S-11 §9-13: raw total ≤ 6 GB); a failed clip keeps them for a retry. --keep-frames keeps them always.

usage: mk_clips.py <segment dir> --tdmux BIN --probe BIN [--formats 1080x1920,1080x1440,1080x1350,1080x1080]
                   [--segment NAME] [--meta k=v ...] [--keep-frames]
writes <seg>/mp4/*.mp4, <seg>/stills/*.{png,jpg}, <seg>/clips.json; prints one line per clip
exit 0 every kept clip muxed · 1 a clip failed (its frames stay) · 2 no session (summary.json / sync.json missing) · 64 usage"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ALIGN = HERE.parent / "td-capture" / "align_audio.py"
DEFAULT_FORMATS = "1080x1920,1080x1440,1080x1350,1080x1080"
STREAMS = (("frames", ""), ("frames-hud", "-hud"))
MAX_AV_ERR = 0.1   # S-11 §6: A/V > 0.1 s rejects a candidate


def read_json(p):
    try:
        return json.loads(Path(p).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def rows(path):
    out = []
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        cols = line.split("\t")
        try:
            out.append((float(cols[0]), cols))
        except ValueError:
            continue
    return out


def parse_formats(s):
    fmts = []
    for f in s.split(","):
        w, _, h = f.partition("x")
        if not (w.isdigit() and h.isdigit()) or int(w) % 2 or int(h) % 2:
            raise ValueError("format %r: WxH, even" % f)
        fmts.append((int(w), int(h)))
    return fmts


def clip_t0(frames_tsv):
    ts = []
    for line in Path(frames_tsv).read_text(encoding="utf-8").splitlines():
        cols = line.split("\t")
        if len(cols) >= 2:
            try:
                ts.append(float(cols[1]))
            except ValueError:
                pass
    return (min(ts), max(ts), len(ts)) if ts else (None, None, 0)


ANCHOR_MARGIN = 1.5   # s of session anchors kept around the clip (dry-1 11.10: all session anchors made every clip
                      # report the session's worst second as its avError)


ANCHOR_CLOCK_TOL = 0.25   # s: an anchor's t and its realtime column are one session clock (t = realtime − t0); a row off
                          # the rows' median by more is dropped (dry-3 12.10: the stop row carried the stop time with
                          # readings taken 2.4 s later, after the raw-budget drain — clip 03 avError 1.52)


def clock_rows(path):
    """(the anchor rows whose t agrees with their own realtime, the number dropped). Rows without a realtime column (older
    sessions) are kept."""
    all_rows = rows(path)
    offsets = []
    for t, cols in all_rows:
        try:
            offsets.append(float(cols[3]) - t)
        except (IndexError, ValueError):
            pass
    if not offsets:
        return all_rows, 0
    median = sorted(offsets)[len(offsets) // 2]
    kept = []
    for t, cols in all_rows:
        try:
            ok = abs(float(cols[3]) - t - median) <= ANCHOR_CLOCK_TOL
        except (IndexError, ValueError):
            ok = True
        if ok:
            kept.append((t, cols))
    return kept, len(all_rows) - len(kept)


def lay_work(seg, clip_dir, stream, t0, work, t1=None):
    """td-capture's video-dir shape for align_audio.py, every time on the clip's own line (t − t0); only the session's
    audio anchors from t0 − ANCHOR_MARGIN to t1 + ANCHOR_MARGIN, so the alignment and its error are the clip's own."""
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)
    with open(work / "frames.tsv", "w", encoding="utf-8") as f:
        for line in (clip_dir / "frames.tsv").read_text(encoding="utf-8").splitlines():
            cols = line.split("\t")
            if len(cols) >= 2 and (clip_dir / stream / ("f-%06d.jpg" % int(cols[0]))).exists():
                f.write("%s\t%.6f\n" % (cols[0], float(cols[1]) - t0))
    with open(work / "audio-sync.tsv", "w", encoding="utf-8") as f:
        f.write("# t_seconds\twav_frames_captured\tdsp_time\trealtime\tdropped_samples   (rebased: t - clip t0 %.6f)\n" % t0)
        anchors, dropped = clock_rows(seg / "audio-sync.tsv")
        for t, cols in anchors:
            if t < t0 - ANCHOR_MARGIN or (t1 is not None and t > t1 + ANCHOR_MARGIN):
                continue
            cols = list(cols)
            cols[0] = "%.6f" % (t - t0)
            f.write("\t".join(cols) + "\n")
    sync = read_json(seg / "sync.json") or {}
    audio = dict(sync.get("audio") or {})
    if audio.get("anchorT") is not None:
        audio["anchorT"] = float(audio["anchorT"]) - t0
    if audio.get("wavOffsetSeconds") is not None:
        audio["wavOffsetSeconds"] = float(audio["wavOffsetSeconds"]) + t0
    (work / "sync.json").write_text(json.dumps({"wp": "WP-110", "clipT0": t0, "audio": audio}) + "\n", encoding="utf-8")
    wav = seg / "audio.wav"
    if wav.exists():
        os.symlink(wav.resolve(), work / "audio.wav")
    audio["anchorsDropped"] = dropped   # the caller's note (not in the work sync.json)
    return audio


def clip_av_error(align, seconds):
    """The worst per-second A/V error among the anchor stretches that overlap the clip [0, seconds] (dry-2 11.10: the margin
    anchors outside the clip must not count). align.json perSecond = [[t_i, err_i], …], stretch i = [t_i, t_{i+1})."""
    rows = [r for r in (align or {}).get("perSecond") or [] if isinstance(r, list) and len(r) == 2]
    worst = 0.0
    for i, (t, err) in enumerate(rows):
        t_next = rows[i + 1][0] if i + 1 < len(rows) else t + 1.0
        if t < seconds and t_next > 0.0:
            worst = max(worst, float(err))
    return round(worst, 3)


def crop_box(w, h, fw, fh):
    x = (w - fw) // 2
    y = (h - fh) // 2
    return x, y, x + fw, y + fh


def crop_stills(clip_dir, seg_name, n, fmts, out_dir):
    try:
        from PIL import Image
    except ImportError:
        return [], "PIL missing: stills not cropped"
    written = []
    for sub, suffix in (("stills", ""), ("stills-hud", "-hud")):
        for png in sorted((clip_dir / sub).glob("*.png")) if (clip_dir / sub).is_dir() else []:
            with Image.open(png) as im:
                im = im.convert("RGB")
                for fw, fh in fmts:
                    if fw > im.width or fh > im.height:
                        continue
                    stem = "%s-%02d-%s-%dx%d%s" % (seg_name, n, png.stem, fw, fh, suffix)
                    c = im.crop(crop_box(im.width, im.height, fw, fh))
                    c.save(out_dir / (stem + ".png"))
                    c.save(out_dir / (stem + ".jpg"), quality=92)
                    written += [stem + ".png", stem + ".jpg"]
    return written, None


def mux_clip(seg, seg_name, clip_dir, clip, fmts, tdmux, probe, keep_frames, log, master, fps=30):
    n = clip["clip"]
    rec = {"segment": seg_name, "clip": n, "triggerKind": clip.get("triggerKind"), "trigger": clip.get("trigger"),
           "peak": clip.get("peak"), "round": clip.get("round"), "weapon": clip.get("weapon"), "killsInClip": clip.get("killsInClip"),
           "maxGroupKills": clip.get("maxGroupKills"), "maxChainKills": clip.get("maxChainKills"),
           "crowdWithin20mAtTrigger": clip.get("crowdWithin20mAtTrigger"), "aliveAtTrigger": clip.get("aliveAtTrigger"),
           "camera": clip.get("camera"), "lookOffsetAtTrigger": clip.get("lookOffsetAtTrigger"), "fps": clip.get("effectiveFps"),
           "hud": clip.get("hud"), "videos": [], "stills": [], "avError": None, "ok": True, "notes": []}
    t0, t1, count = clip_t0(clip_dir / "frames.tsv")
    if t0 is None:
        rec["ok"] = False; rec["notes"].append("no frames listed")
        return rec
    rec["seconds"] = round(t1 - t0, 3)
    rec["skippedFormats"] = ["%dx%d" % f for f in fmts if f[0] > master[0] or f[1] > master[1]]   # gt NOTE: never silently
    fmts = [(fw, fh) for fw, fh in fmts if fw <= master[0] and fh <= master[1]]   # crops only — nothing is scaled up
    for stream, suffix in STREAMS:
        sdir = clip_dir / stream
        if not sdir.is_dir() or not any(sdir.glob("f-*.jpg")):
            continue
        work = clip_dir / ("work" + suffix)
        audio = lay_work(seg, clip_dir, stream, t0, work, t1)
        if audio.get("anchorsDropped") and not any(x.startswith("anchors dropped") for x in rec["notes"]):
            rec["notes"].append("anchors dropped: %d (t off its own realtime)" % audio["anchorsDropped"])
        mux = ["--frames", str(sdir), "--times", str(work / "frames.tsv"), "--fps", str(fps), "--audio-kbps", "256"]
        r = subprocess.run([sys.executable, str(ALIGN), str(work), str(work / "audio-aligned.wav")], capture_output=True, text=True)
        if r.returncode == 0:
            mux += ["--wav", str(work / "audio-aligned.wav"), "--wav-offset", "0"]
            err = clip_av_error(read_json(work / "align.json"), t1 - t0)
            rec["avError"] = err if rec["avError"] is None else max(rec["avError"], err)
        elif (seg / "audio.wav").exists() and audio.get("wavOffsetSeconds") is not None:
            mux += ["--wav", str(seg / "audio.wav"), "--wav-offset", "%.6f" % float(audio["wavOffsetSeconds"])]
            rec["notes"].append("audio not aligned per anchor (%s) — single offset" % (r.stderr.strip().splitlines() or ["?"])[-1])
        else:
            rec["notes"].append("no audio")
        for fw, fh in fmts:
            name = "%s-%02d-%dx%d%s.mp4" % (seg_name, n, fw, fh, suffix)
            out = seg / "mp4" / name
            cmd = [tdmux] + mux + ["--out", str(out), "--mbps", "16" if fh >= 1920 else "12", "--crop", "%dx%d" % (fw, fh)]
            r = subprocess.run(cmd, capture_output=True, text=True)
            log.write("tdmux %s: exit %d %s\n" % (name, r.returncode, (r.stderr or r.stdout).strip().splitlines()[-1:] if (r.stderr or r.stdout) else ""))
            if r.returncode != 0:
                rec["ok"] = False; rec["notes"].append("tdmux %s exit %d" % (name, r.returncode)); continue
            p = subprocess.run([probe, str(out)], capture_output=True, text=True)
            info = None
            try:
                info = json.loads(p.stdout)
            except ValueError:
                pass
            video = (info or {}).get("video") or {}
            audio_t = (info or {}).get("audio") or {}
            good = p.returncode == 0 and video.get("width") == fw and video.get("height") == fh and \
                abs((video.get("duration") or 0) - (t1 - t0)) <= 0.5 and (audio_t.get("codec") == "aac" or "no audio" in rec["notes"])
            rec["videos"].append({"file": "mp4/" + name, "probe": info, "ok": good})
            if not good:
                rec["ok"] = False; rec["notes"].append("probe %s: %s" % (name, "unexpected size / duration / no AAC"))
    stills, note = crop_stills(clip_dir, seg_name, n, fmts, seg / "stills")
    rec["stills"] = ["stills/" + s for s in stills]
    if note:
        rec["notes"].append(note)
    if rec["avError"] is not None and rec["avError"] > MAX_AV_ERR:
        rec["notes"].append("A/V error %.3f s > %.1f s (S-11 §6: not a candidate)" % (rec["avError"], MAX_AV_ERR))
    if rec["ok"] and rec["videos"] and not keep_frames:
        for stream, suffix in STREAMS:
            for d in (clip_dir / stream, clip_dir / ("work" + suffix)):
                if d.is_dir():
                    if (d / "align.json").exists():
                        shutil.copy2(d / "align.json", clip_dir / ("align" + suffix + ".json"))
                    shutil.rmtree(d)
        rec["framesRemoved"] = True
    return rec


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("seg")
    ap.add_argument("--tdmux", required=True)
    ap.add_argument("--probe", required=True)
    ap.add_argument("--formats", default=DEFAULT_FORMATS)
    ap.add_argument("--segment", default=None)
    ap.add_argument("--meta", nargs="*", default=[])
    ap.add_argument("--keep-frames", action="store_true")
    a = ap.parse_args(argv)
    seg = Path(a.seg)
    try:
        fmts = parse_formats(a.formats)
    except ValueError as e:
        print("mk_clips: %s" % e, file=sys.stderr)
        return 64
    summary = read_json(seg / "summary.json")
    if summary is None or read_json(seg / "sync.json") is None:
        print("mk_clips: %s has no summary.json / sync.json — the capture agent did not run its session" % seg, file=sys.stderr)
        return 2
    name = a.segment or seg.name
    meta = dict(kv.split("=", 1) for kv in a.meta if "=" in kv)
    (seg / "mp4").mkdir(exist_ok=True)
    (seg / "stills").mkdir(exist_ok=True)
    records, rc = [], 0
    with open(seg / "mk_clips.log", "a", encoding="utf-8") as log:
        for clip_json in sorted((seg / "clips").glob("*/clip.json")):
            clip = read_json(clip_json)
            if not clip or not clip.get("kept"):
                # gt NOTE (11.10): a clip the agent dropped (shorter than 5 s) is never muxed — its raw frames go too
                if clip and not a.keep_frames:
                    for stream, _ in STREAMS:
                        if (clip_json.parent / stream).is_dir():
                            shutil.rmtree(clip_json.parent / stream)
                continue
            rec = mux_clip(seg, name, clip_json.parent, clip, fmts, a.tdmux, a.probe, a.keep_frames, log, tuple(summary.get("master") or (1080, 1920)),
                           int(summary.get("fps") or 30))
            rec.update({k: v for k, v in meta.items() if k not in rec})
            records.append(rec)
            rc = rc or (0 if rec["ok"] else 1)
            print("clip %02d %-11s %5.1fs kills %-3s videos %d stills %d av %s %s" % (
                rec["clip"], rec.get("triggerKind") or "?", rec.get("seconds") or 0, rec.get("killsInClip"), len(rec["videos"]),
                len(rec["stills"]), "-" if rec["avError"] is None else "%.3f" % rec["avError"], "ok" if rec["ok"] else "FAIL " + "; ".join(rec["notes"])))
    out = {"wp": "WP-110", "segment": name, "meta": meta, "formats": ["%dx%d" % f for f in fmts], "stopReason": summary.get("stopReason"),
           "sessionFps": (summary.get("frames") or {}).get("sessionFps"), "events": summary.get("events"), "clips": records}
    (seg / "clips.json").write_text(json.dumps(out, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
