#!/usr/bin/env python3
"""WP-099 (S-01, C-00 07.10) · td-capture: the WAV second that matches video t0, for tdmux --wav-offset. The agent writes
it as sync.json → audio.wavOffsetSeconds (= AudioOutputCapture.FramesCaptured at t0 / output sample rate). Prints the
offset; warns on stderr when summary.json's audio drift is over 0.05 s (WAV gaps — audio-sync.tsv has 1 s anchors).
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
    try:
        drift = ((json.loads((d / "summary.json").read_text(encoding="utf-8")).get("audio") or {}).get("driftSeconds"))
        if drift is not None and abs(drift) > 0.05:
            print("WARNING audio drift %.3f s > 0.05 s: the WAV has gaps; audio-sync.tsv holds 1 s anchors" % drift,
                  file=sys.stderr)
    except (OSError, ValueError):
        pass
    print("%.6f" % float(off))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
