#!/bin/zsh
# WP-061 · real development-player B11/M11-M16 acceptance evidence using the existing probe.
set -u
source "${0:A:h:h}/dd-env.sh"
APP="${1:-}"; OUT="${2:-}"
[ -d "$APP" ] && [ -n "$OUT" ] || { echo "usage: economy-acceptance.sh <development.app> <out-dir>" >&2; exit 64; }
BIN="$APP/Contents/MacOS/$DD_PRODUCT"
[ -x "$BIN" ] || { echo "player binary missing: $BIN" >&2; exit 66; }
mkdir -p "$OUT/profile"
DD_TIMELINE="$OUT/timeline.txt"
trap 'dd_slot_release smoke' EXIT
dd_slot_acquire smoke || exit $?   # D1 review MUST-2 / M-07c: slot.sh's code (75 timeout, 64 usage) now reaches the caller
# Y2 (C-00 30.09): through tools/player-run.sh (player slot) with the UI files and telemetry in <out>; the caller keeps
# tools/userdata-guard.sh snapshot/restore --scope player around this script (rc.sh does) — -screen-fullscreen 0 writes
# the Screenmanager keys of the real player PlayerPrefs.
"$DD_TOOLS_DIR/player-run.sh" "$BIN" -screen-fullscreen 0 -ddProfilePath "$OUT/profile/prototype-profile.json" \
  -ddUiDataRoot "$OUT/ui-data" -ddTelemetryDir "$OUT/telemetry" -ddEconomyProbe "$OUT" -logFile "$OUT/player.log"
CODE=$?
dd_slot_release smoke
trap - EXIT
[ "$CODE" -eq 0 ] || exit "$CODE"
python3 - "$OUT" <<'PY'
import glob, json, os, sys
root = sys.argv[1]
files = sorted(glob.glob(os.path.join(root, "[0-9][0-9]-*.json")))
stages = []
for path in files:
    with open(path) as handle:
        stages.append(json.load(handle))
ok = len(stages) == 11 and all(stage.get("pass") for stage in stages)
print(f"{'PASS' if ok else 'FAIL'} economy acceptance stages={len(stages)} passed={sum(bool(s.get('pass')) for s in stages)}")
raise SystemExit(0 if ok else 1)
PY
