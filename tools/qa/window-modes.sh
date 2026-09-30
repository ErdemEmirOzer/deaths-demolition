#!/bin/zsh
# WP-061 · run the opt-in development-player C2-C4 window-mode gate.
set -u
source "${0:A:h:h}/dd-env.sh"
APP="${1:-}"; OUT="${2:-}"
[ -d "$APP" ] && [ -n "$OUT" ] || { echo "usage: window-modes.sh <development.app> <out-dir>" >&2; exit 64; }
BIN="$APP/Contents/MacOS/$DD_PRODUCT"
[ -x "$BIN" ] || { echo "player binary missing: $BIN" >&2; exit 66; }
mkdir -p "$OUT/profile"
DD_TIMELINE="$OUT/timeline.txt"
trap 'dd_slot_release smoke' EXIT
dd_slot_acquire smoke
# Y2 (C-00 30.09): through tools/player-run.sh (player slot) with the UI files and telemetry in <out>; the caller keeps
# tools/userdata-guard.sh snapshot/restore --scope player around this script (rc.sh does) — -screen-fullscreen 0 writes
# the Screenmanager keys of the real player PlayerPrefs.
"$DD_TOOLS_DIR/player-run.sh" "$BIN" -screen-fullscreen 0 -ddProfilePath "$OUT/profile/prototype-profile.json" \
  -ddUiDataRoot "$OUT/ui-data" -ddTelemetryDir "$OUT/telemetry" -ddQaWindowModes "$OUT" -logFile "$OUT/player.log"
CODE=$?
dd_slot_release smoke
trap - EXIT
[ "$CODE" -eq 0 ] && [ -s "$OUT/qa-report.json" ] || exit 1
python3 - "$OUT/qa-report.json" <<'PY'
import json, sys
with open(sys.argv[1]) as handle:
    report = json.load(handle)
raise SystemExit(0 if report.get("pass") else 1)
PY
