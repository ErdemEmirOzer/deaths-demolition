#!/bin/zsh
# WP-061 · M4 release-candidate acceptance/regression — one repeatable entry point.
#
# Usage: tools/qa/rc.sh [out-dir] [--rounds 12] [--seed 61] [--perf-runs 3] [--window 60]
#                       [--diagnostic-continue]
#
# The chain is deliberately fail-fast by default: the first failed stage stops later expensive
# work. --diagnostic-continue keeps running independent evidence stages after a failure while the
# final verdict remains failed. Every attempted stage keeps its exit code, output and artefacts;
# rc_aggregate.py always runs.
set -u

HERE="${0:A:h}"
REPO="${DD_REPO:-${HERE:h:h}}"
source "$REPO/tools/dd-env.sh"

OUT=""
ROUNDS=12
SEED=61
PERF_RUNS=3
WINDOW=60
DIAGNOSTIC_CONTINUE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --rounds) ROUNDS="$2"; shift 2 ;;
    --seed) SEED="$2"; shift 2 ;;
    --perf-runs) PERF_RUNS="$2"; shift 2 ;;
    --window) WINDOW="$2"; shift 2 ;;
    --diagnostic-continue) DIAGNOSTIC_CONTINUE=1; shift ;;
    --*) echo "unknown option: $1" >&2; exit 64 ;;
    *) [ -z "$OUT" ] || { echo "unexpected argument: $1" >&2; exit 64; }; OUT="$1"; shift ;;
  esac
done
[ "$ROUNDS" -ge 12 ] || { echo "WP-061 RC requires --rounds >= 12" >&2; exit 64; }
[ -n "$OUT" ] || OUT="$DD_BUILD_ROOT/rc-$(dd_utc)"
if [ -d "$OUT" ] && [ -n "$(ls -A "$OUT" 2>/dev/null)" ]; then
  echo "refusing non-empty RC output directory: $OUT" >&2
  exit 73
fi
mkdir -p "$OUT/stages"
TIMELINE="$OUT/timeline.txt"
START_EPOCH=$(date +%s)
FAILED=0
FAILED_STAGE=""

log() { echo "$(date -u +%FT%TZ) $*" | tee -a "$TIMELINE"; }
stage() {
  local name="$1"; shift
  local dir="$OUT/stages/$name"
  mkdir -p "$dir"
  printf '%q ' "$@" > "$dir/command.txt"; echo >> "$dir/command.txt"
  log "START $name"
  "$@" > "$dir/stdout.log" 2>&1
  local code=$?
  echo "$code" > "$dir/exit.txt"
  log "END $name exit=$code"
  if [ "$code" -ne 0 ]; then
    if [ "$FAILED" -eq 0 ]; then FAILED="$code"; FAILED_STAGE="$name"; fi
    [ "$DIAGNOSTIC_CONTINUE" -eq 1 ] && return 0
    return "$code"
  fi
  return 0
}

blocked_stage() {
  local name="$1" reason="$2"
  local dir="$OUT/stages/$name"
  mkdir -p "$dir"
  echo "$reason" > "$dir/stdout.log"
  echo "not run: $reason" > "$dir/command.txt"
  echo 1 > "$dir/exit.txt"
  log "BLOCKED $name: $reason"
  if [ "$FAILED" -eq 0 ]; then FAILED=1; FAILED_STAGE="$name"; fi
  [ "$DIAGNOSTIC_CONTINUE" -eq 1 ] && return 0
  return 1
}

PROJECT_LOCK="/tmp/dd-rc-project-$(printf '%s' "$DD_PROJECT" | cksum | awk '{print $1}').lock"
if ! mkdir "$PROJECT_LOCK" 2>/dev/null; then
  echo "another RC owns this project path: $PROJECT_LOCK" >&2
  exit 75
fi
echo "$$ $DD_PROJECT" > "$PROJECT_LOCK/owner.txt"

finalize() {
  python3 "$HERE/rc_aggregate.py" "$OUT" --repo "$REPO" --rounds "$ROUNDS" --start-epoch "$START_EPOCH" --failed-stage "$FAILED_STAGE"
  local agg=$?
  [ "$FAILED" -ne 0 ] && return "$FAILED"
  return "$agg"
}
cleanup() { rm -f "$PROJECT_LOCK/owner.txt" 2>/dev/null || true; rmdir "$PROJECT_LOCK" 2>/dev/null || true; }
trap 'finalize >/dev/null 2>&1 || true; cleanup' EXIT INT TERM

{
  echo "repo=$REPO"
  echo "head=$(git -C "$REPO" rev-parse HEAD 2>/dev/null)"
  echo "branch=$(git -C "$REPO" branch --show-current 2>/dev/null)"
  echo "unity=$DD_UNITY"
  echo "project=$DD_PROJECT"
  echo "rounds=$ROUNDS seed=$SEED perfRuns=$PERF_RUNS window=$WINDOW"
  echo "diagnosticContinue=$DIAGNOSTIC_CONTINUE"
  echo "startEpoch=$START_EPOCH"
  uname -a
  "$REPO/tools/slot.sh" status
} > "$OUT/environment.txt" 2>&1

[ -x "$DD_UNITY" ] || { log "Unity missing: $DD_UNITY"; FAILED=66; FAILED_STAGE="preflight"; exit "$FAILED"; }
[ -x "$REPO/tools/test.sh" ] && [ -x "$REPO/tools/build.sh" ] && [ -x "$REPO/tools/qa/regression.sh" ] || {
  log "required QA tools missing or not executable"; FAILED=66; FAILED_STAGE="preflight"; exit "$FAILED";
}

stage full-editmode "$REPO/tools/test.sh" editmode --out "$OUT/full-editmode" --timeout-min 30 || exit "$FAILED"
stage full-playmode "$REPO/tools/test.sh" playmode --out "$OUT/full-playmode" --timeout-min 60 || exit "$FAILED"
stage release-smoke "$REPO/tools/build.sh" release --smoke --out "$OUT/release" || exit "$FAILED"

# Full suites above already include the QA EditMode and Armor PlayMode tests. Hand those XML files to
# the M3 aggregate so regression.sh can avoid repeating the same tests while still enforcing them.
export DD_EXTERNAL_EDITMODE_XML="$OUT/full-editmode/editmode.xml"
export DD_EXTERNAL_PLAYMODE_XML="$OUT/full-playmode/playmode.xml"
export DD_QA_PACKAGE="WP-061"
export DD_REQUIRE_ULTRA_EVIDENCE="1"
export DD_READABILITY_ROUNDS="1,4,8,10,12"
export DD_BOT_EXTRA="-ddQaBotFrames $OUT/m3/bot/tier-frames -ddUiCapture hud -ddUiCaptureOut $OUT/m3/bot/ui -ddUiCaptureRounds 1,2,4,8,12"
export DD_PLAYER_TIMEOUT="${DD_PLAYER_TIMEOUT:-7200}"
stage m3-regression "$REPO/tools/qa/regression.sh" "$OUT/m3" --rounds "$ROUNDS" --seed "$SEED" --perf-runs "$PERF_RUNS" --window "$WINDOW" --skip-tests --steps build,spawnmix,bot,perf,readability || exit "$FAILED"
stage soak-exact python3 "$HERE/soak_gate.py" "$OUT/m3/bot/bot-summary.json" --rounds "$ROUNDS" || exit "$FAILED"

DEV_APP="$OUT/m3/build-dev/DeathsDemolition.app"
if [ -d "$DEV_APP" ]; then
  stage economy-acceptance "$REPO/tools/qa/economy-acceptance.sh" "$DEV_APP" "$OUT/economy-acceptance" || exit "$FAILED"
  stage window-modes "$REPO/tools/qa/window-modes.sh" "$DEV_APP" "$OUT/window-modes" || exit "$FAILED"
else
  blocked_stage economy-acceptance "development app missing: $DEV_APP" || exit "$FAILED"
  blocked_stage window-modes "development app missing: $DEV_APP" || exit "$FAILED"
fi
stage log-scan python3 "$HERE/log_scan.py" "$OUT" --since "$START_EPOCH" || exit "$FAILED"

log "RC chain complete"
trap - EXIT INT TERM
finalize
CODE=$?
cleanup
exit "$CODE"
