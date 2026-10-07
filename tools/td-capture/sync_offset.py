#!/usr/bin/env python3
"""WP-099 (S-01, C-00 07.10) · td-capture: the WAV second that matches video t0, for tdmux --wav-offset. The agent writes
it as sync.json → audio.wavOffsetSeconds (= AudioOutputCapture.FramesCaptured at t0 / output sample rate). Prints the
offset. The A/V drift is judged in manifest.py from audio-sync.tsv (gt MUST-1, 08.10: summary.json's driftSeconds is read
after the frame drain and misleads).
usage: sync_offset.py <video dir>   exit 0 printed · 2 no offset (null / missing)"""
import json
import sys
from pathlib import Path


def main(argv):
    d = Path(argv[1])
    try:
        sync = json.loads((d / "sync.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        print("sync.json unreadable: %s" % e, file=sys.stderr)
        return 2
    off = (sync.get("audio") or {}).get("wavOffsetSeconds")
    if off is None:
        print("sync.json has no audio.wavOffsetSeconds (was -ddAudioCapture passed?)", file=sys.stderr)
        return 2
    print("%.6f" % float(off))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
