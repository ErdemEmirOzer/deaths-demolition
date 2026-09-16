#!/bin/zsh
# WP-025 · tools/build.sh {dev|release} [--smoke] [--out DIR] [--no-wait]
#
# One command, repeatable, for every session and for S-10's regression automation:
#   build.sh release --smoke      release player + main-menu/run smoke + user-data guard
#   build.sh dev                  development player (probes enabled: -ddProbe …)
#
# Output: <out>/DeathsDemolition.app, <out>/build.log, <out>/build-report.json (from the
# Editor), <out>/report.json (script summary incl. smoke) and <out>/timeline.txt.
# Default <out> = $DD_BUILD_ROOT/<mode>-<utc>. The version stamp (git describe, commit,
# branch, date, mode, Unity) is written into the player by DD.Prototype.Core.EditorTools.DdBuild.
#
# Exit codes (contract for callers):
#   0 ok · 10 build failed · 20 smoke failed · 30 user-data guard failed · 64 usage · 75 machine busy (--no-wait)
# Machine rule: a Unity slot is taken through tools/slot.sh (max 2 Unity batch on this machine) and released after the build.
set -u
source "${0:A:h}/dd-env.sh"
MODE="${1:-}"; shift 2>/dev/null || true
[ "$MODE" = "dev" ] || [ "$MODE" = "release" ] || { echo "usage: build.sh {dev|release} [--smoke] [--out DIR] [--no-wait]" >&2; exit 64; }
SMOKE=0; OUT=""; NOWAIT=0
while [ $# -gt 0 ]; do
  case "$1" in
    --smoke) SMOKE=1 ;;
    --out) OUT="$2"; shift ;;
    --no-wait) NOWAIT=1 ;;
    *) echo "unknown option: $1" >&2; exit 64 ;;
  esac
  shift
done
[ -n "$OUT" ] || OUT="$DD_BUILD_ROOT/$MODE-$(dd_utc)"
mkdir -p "$OUT"; DD_TIMELINE="$OUT/timeline.txt"
APP="$OUT/$DD_APP_NAME"; LOG="$OUT/build.log"; BREPORT="$OUT/build-report.json"; REPORT="$OUT/report.json"
dd_log "build.sh mode=$MODE smoke=$SMOKE out=$OUT repo=$DD_REPO head=$(git -C "$DD_REPO" rev-parse --short=12 HEAD 2>/dev/null)"

# Machine slot: tools/slot.sh (blocks until one of the 2 Unity slots is free; --no-wait refuses instead).
if [ "$NOWAIT" -eq 1 ] && [ "$(dd_unity_count)" -ge "$DD_MAX_UNITY" ]; then dd_log "busy: $(dd_unity_count) Unity batch processes (limit $DD_MAX_UNITY)"; exit 75; fi
trap 'dd_slot_release' EXIT
dd_slot_acquire

T0=$(date +%s)
"$DD_UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$DD_PROJECT" \
  -executeMethod DD.Prototype.Core.EditorTools.DdBuild.Build -ddMode "$MODE" -ddOut "$APP" -ddReport "$BREPORT" \
  -logFile "$LOG"
X=$?; T1=$(date +%s)
APPKB=$( [ -d "$APP" ] && du -sk "$APP" | cut -f1 || echo 0 )
ERRS=$(grep -c 'error CS' "$LOG" 2>/dev/null); ERRS=${ERRS:-0}
dd_log "build exit=$X seconds=$((T1-T0)) appKB=$APPKB errorsCS=$ERRS"
dd_slot_release   # the editor is gone; the smoke step takes its own slot
BUILD_OK=0; [ "$X" -eq 0 ] && [ -d "$APP" ] && grep -q '"result": "Succeeded"' "$BREPORT" 2>/dev/null && BUILD_OK=1
# WP-046: build footprint (categories, top assets, folder roll-up, cap check) from the Unity build log
if [ "$BUILD_OK" -eq 1 ] && [ -x "$DD_TOOLS_DIR/build-report.sh" ]; then
  "$DD_TOOLS_DIR/build-report.sh" "$OUT" --top 25 --cap "${DD_BUILD_CAP_MB:-450}" | head -1 | while IFS= read -r l; do dd_log "$l"; done
fi

SMOKE_STATUS="skipped"; SMOKE_EXIT=0
if [ "$BUILD_OK" -eq 1 ] && [ "$SMOKE" -eq 1 ]; then
  "$DD_TOOLS_DIR/smoke.sh" "$APP" "$OUT" --mode "$MODE"; SMOKE_EXIT=$?
  case "$SMOKE_EXIT" in 0) SMOKE_STATUS="ok" ;; 20) SMOKE_STATUS="failed" ;; 30) SMOKE_STATUS="userdata-guard-failed" ;; *) SMOKE_STATUS="error($SMOKE_EXIT)" ;; esac
fi

CODE=0
[ "$BUILD_OK" -eq 1 ] || CODE=10
[ "$CODE" -eq 0 ] && [ "$SMOKE_EXIT" -ne 0 ] && CODE=$SMOKE_EXIT
STAMP=$( [ -f "$BREPORT" ] && grep -o '"describe": "[^"]*"' "$BREPORT" | head -1 | cut -d'"' -f4 || echo "" )
cat > "$REPORT" <<JSON
{
  "mode": "$MODE",
  "ok": $([ "$CODE" -eq 0 ] && echo true || echo false),
  "exitCode": $CODE,
  "build": { "ok": $([ "$BUILD_OK" -eq 1 ] && echo true || echo false), "unityExit": $X, "seconds": $((T1-T0)), "appKB": $APPKB, "errorsCS": $ERRS, "app": "$APP", "log": "$LOG", "report": "$BREPORT", "stamp": "$STAMP" },
  "smoke": { "status": "$SMOKE_STATUS", "exitCode": $SMOKE_EXIT, "report": "$OUT/smoke-report.json" },
  "footprint": "$OUT/footprint.json",
  "head": "$(git -C "$DD_REPO" rev-parse HEAD 2>/dev/null)",
  "utc": "$(date -u +%FT%TZ)"
}
JSON
dd_log "build.sh done exit=$CODE report=$REPORT"
echo "exit=$CODE mode=$MODE seconds=$((T1-T0)) appKB=$APPKB stamp=$STAMP smoke=$SMOKE_STATUS report=$REPORT"
exit $CODE
