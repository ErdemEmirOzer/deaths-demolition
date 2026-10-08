#!/usr/bin/env python3
"""WP-099 (S-01, C-00 08.10) · td-capture: lay the game WAV on the video's real-time line, anchor by anchor.

The capture agent writes video/audio-sync.tsv: one row per second, "t<TAB>wav_frames_captured…" (t = real seconds from
video t0). The WAV only gets samples while an audio listener mixes — a scene swap (boot: the loading screen) or a loaded
machine leaves gaps, so one offset (sync.json wavOffsetSeconds) puts the sound up to seconds off later in the clip
(smoke-boot 08.10: -1.4 s). Here every anchor second [t_i, t_i+1) gets the WAV frames [F_i, F_i+1) placed from t_i on:
fewer frames than real time → silence pads the end of that second; more → the excess is trimmed. Frames before the
first anchor play up to it. The output WAV starts at video t0 (tdmux --wav-offset 0); within a second that had a gap
the sound can be early by at most that gap, at every anchor it is exact.

usage: align_audio.py <video dir> <out.wav>   (reads audio.wav, audio-sync.tsv, sync.json, frames.tsv)
writes <video dir>/align.json {anchors, seconds, paddedSeconds, trimmedSeconds, maxSecondErr}
exit 0 written · 2 missing / unusable input (the caller falls back to the single offset)"""
import json
import sys
import wave
from pathlib import Path


def anchors(video):
    rows = []
    for line in (video / "audio-sync.tsv").read_text(encoding="utf-8").splitlines():
        cols = line.split("\t")
        if line.startswith("#") or len(cols) < 2:
            continue
        try:
            t, f = float(cols[0]), int(cols[1])
        except ValueError:
            continue
        if f > 0:   # 0: no sample yet (the WAV starts later)
            rows.append((t, f))
    audio = (json.loads((video / "sync.json").read_text(encoding="utf-8")).get("audio") or {})
    if audio.get("anchorT") is not None and audio.get("anchorFrames"):   # the first moment the WAV had samples
        rows.append((float(audio["anchorT"]), int(audio["anchorFrames"])))
    rows = sorted(set(rows))
    out = []
    for t, f in rows:   # monotonic in both t and frames
        if not out or (t > out[-1][0] and f >= out[-1][1]):
            out.append((t, f))
    return out


def video_seconds(video, fps=30):
    last = 0.0
    for line in (video / "frames.tsv").read_text(encoding="utf-8").splitlines():
        cols = line.split("\t")
        try:
            last = max(last, float(cols[1]))
        except (IndexError, ValueError):
            continue
    return last + 1.0 / fps


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 64
    video, dst = Path(argv[1]), Path(argv[2])
    try:
        src = wave.open(str(video / "audio.wav"), "rb")
        A = anchors(video)
        total_s = video_seconds(video)
    except (OSError, ValueError, wave.Error) as e:
        print("align_audio: %s" % e, file=sys.stderr)
        return 2
    if not A:
        print("align_audio: no usable anchor in audio-sync.tsv", file=sys.stderr)
        return 2
    ch, width, rate, n_src = src.getnchannels(), src.getsampwidth(), src.getframerate(), src.getnframes()
    pcm = src.readframes(n_src); src.close()
    fb = ch * width   # bytes per frame
    n_out = int(round(total_s * rate))
    out = bytearray(n_out * fb)

    def place(at_frame, f0, count):
        """copy WAV frames [f0, f0+count) to output frame at_frame (both clipped)."""
        if count <= 0:
            return
        if at_frame < 0:
            f0, count, at_frame = f0 - at_frame, count + at_frame, 0
        count = min(count, n_out - at_frame, n_src - f0)
        if count > 0 and f0 >= 0:
            out[at_frame * fb:(at_frame + count) * fb] = pcm[f0 * fb:(f0 + count) * fb]

    t0, f0 = A[0]
    place(int(round(t0 * rate)) - f0, 0, f0)   # what the WAV held before the first anchor, ending at it
    padded = trimmed = 0
    worst = 0.0
    for i, (t, f) in enumerate(A):
        t_next = A[i + 1][0] if i + 1 < len(A) else total_s
        f_next = A[i + 1][1] if i + 1 < len(A) else n_src
        need, have = int(round((t_next - t) * rate)), f_next - f
        place(int(round(t * rate)), f, min(need, have))
        if i + 1 < len(A):   # the last stretch simply runs out — not a gap
            padded += max(0, need - have); trimmed += max(0, have - need)
            worst = max(worst, abs(need - have) / rate)
    with wave.open(str(dst), "wb") as w:
        w.setnchannels(ch); w.setsampwidth(width); w.setframerate(rate); w.writeframes(bytes(out))
    rec = {"anchors": len(A), "seconds": round(n_out / rate, 3), "paddedSeconds": round(padded / rate, 3),
           "trimmedSeconds": round(trimmed / rate, 3), "maxSecondErr": round(worst, 3), "wav": dst.name}
    (video / "align.json").write_text(json.dumps(rec) + "\n", encoding="utf-8")
    print(json.dumps(rec))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
